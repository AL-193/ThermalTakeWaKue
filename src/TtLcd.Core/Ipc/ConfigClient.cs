using System.IO.Pipes;

namespace TtLcd.Ipc;

/// <summary>Talks to the running ttlcd service. Needs no administrator rights.</summary>
public sealed class ConfigClient
{
    // A full sensor read can take over a second.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public Task<IpcResponse> GetStateAsync(bool allSensors, CancellationToken cancellationToken = default) =>
        SendAsync(new IpcRequest(IpcCommands.GetState, allSensors), cancellationToken);

    public Task<IpcResponse> SetConfigAsync(AppConfig config, CancellationToken cancellationToken = default) =>
        SendAsync(new IpcRequest(IpcCommands.SetConfig, Config: config), cancellationToken);

    private static async Task<IpcResponse> SendAsync(IpcRequest request, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(Timeout);

        await using var pipe = new NamedPipeClientStream(".", IpcProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(cts.Token);
        await IpcProtocol.WriteAsync(pipe, request, cts.Token);
        return await IpcProtocol.ReadAsync<IpcResponse>(pipe, cts.Token);
    }
}
