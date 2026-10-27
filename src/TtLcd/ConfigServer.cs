using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using TtLcd.Ipc;

namespace TtLcd;

/// <summary>
/// Serves the settings app (<c>ttlcd-settings</c>) over a local named pipe:
/// reports sensors and settings, and validates, saves and applies new settings.
/// </summary>
internal sealed class ConfigServer(LcdService service, string configPath)
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        NamedPipeServerStream pipe;
        try
        {
            pipe = CreatePipe();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Settings connection unavailable ({e.Message}). Is another ttlcd instance running?");
            return;
        }

        // One pipe instance, reused for every client, so nobody else can grab the name in between.
        await using (pipe)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await pipe.WaitForConnectionAsync(cancellationToken);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(RequestTimeout);

                    var request = await IpcProtocol.ReadAsync<IpcRequest>(pipe, timeout.Token);
                    var response = await HandleAsync(request, timeout.Token);
                    await IpcProtocol.WriteAsync(pipe, response, timeout.Token);
                    // Wait for the client to close, so disconnecting does not discard the unread response.
                    await pipe.ReadAtLeastAsync(new byte[1], 1, throwOnEndOfStream: false, timeout.Token);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception e) when (e is IOException or InvalidDataException or JsonException or OperationCanceledException)
                {
                    Log.Warn($"Settings request failed: {e.Message}");
                }
                finally
                {
                    try
                    {
                        pipe.Disconnect();
                    }
                    catch (InvalidOperationException)
                    {
                        // Never connected.
                    }
                }
            }
        }
    }

    private async Task<IpcResponse> HandleAsync(IpcRequest request, CancellationToken cancellationToken)
    {
        switch (request.Command)
        {
            case IpcCommands.GetState:
                var sensors = request.AllSensors
                    ? await service.ReadAllSensorsAsync().WaitAsync(cancellationToken)
                    : service.LatestReadings;
                return new IpcResponse(true, Config: service.Config, Sensors: sensors, Selection: service.Selection);

            case IpcCommands.SetConfig when request.Config is { } config:
                var all = await service.ReadAllSensorsAsync().WaitAsync(cancellationToken);
                if (ConfigRules.Validate(config, all) is { } error)
                    return new IpcResponse(false, error);
                config.Save(configPath);
                service.ApplyConfig(config);
                Log.Info($"Settings saved to {configPath}.");
                return new IpcResponse(true, Config: config);

            default:
                return new IpcResponse(false, $"Unknown command '{request.Command}'.");
        }
    }

    private static NamedPipeServerStream CreatePipe()
    {
        if (OperatingSystem.IsWindows())
            return CreateWindowsPipe();

        var pipe = new NamedPipeServerStream(
            IpcProtocol.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        // On Linux .NET backs the pipe with a Unix domain socket in the temp directory.
        // The service runs as root; let local users connect, like on Windows.
        var socket = Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + IpcProtocol.PipeName);
        if (File.Exists(socket))
        {
            File.SetUnixFileMode(socket,
                UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite);
        }
        return pipe;
    }

    [SupportedOSPlatform("windows")]
    private static NamedPipeServerStream CreateWindowsPipe()
    {
        var security = new PipeSecurity();
        // Deny entries take precedence: no access over the network.
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        if (WindowsIdentity.GetCurrent().User is { } self)
            security.AddAccessRule(new PipeAccessRule(self, PipeAccessRights.FullControl, AccessControlType.Allow));
        // Logged-in users may read sensors and change the LCD settings.
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.InteractiveSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            IpcProtocol.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
    }
}
