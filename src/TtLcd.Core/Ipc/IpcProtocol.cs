using System.Text.Json;
using System.Text.Json.Serialization;
using TtLcd.Sensors;

namespace TtLcd.Ipc;

public static class IpcCommands
{
    public const string GetState = "get-state";
    public const string SetConfig = "set-config";
}

/// <param name="AllSensors">Read every sensor now instead of returning the last (focused) values.</param>
public sealed record IpcRequest(string Command, bool AllSensors = false, AppConfig? Config = null);

public sealed record IpcResponse(
    bool Ok,
    string? Error = null,
    AppConfig? Config = null,
    IReadOnlyList<SensorReading>? Sensors = null,
    IReadOnlyDictionary<DisplayRole, string>? Selection = null);

/// <summary>
/// Local connection between the settings app and the running ttlcd: a named pipe
/// (a Unix domain socket on Linux) carrying one JSON request and one JSON response,
/// each terminated by a newline.
/// </summary>
public static class IpcProtocol
{
    public const string PipeName = "ttlcd";

    private const int MaxMessageBytes = 1 << 20;

    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
        // LibreHardwareMonitor reports NaN for some sensors.
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static async Task WriteAsync<T>(Stream stream, T message, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(message, Json), cancellationToken);
        await stream.WriteAsync("\n"u8.ToArray(), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        using var message = new MemoryStream();
        var chunk = new byte[4096];
        while (true)
        {
            int read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
                break;
            int end = Array.IndexOf(chunk, (byte)'\n', 0, read);
            message.Write(chunk, 0, end >= 0 ? end : read);
            if (end >= 0)
                break;
            if (message.Length > MaxMessageBytes)
                throw new InvalidDataException("Message too large.");
        }

        if (message.Length == 0)
            throw new EndOfStreamException("Connection closed without a message.");
        return JsonSerializer.Deserialize<T>(message.GetBuffer().AsSpan(0, (int)message.Length), Json)
            ?? throw new InvalidDataException("Empty message.");
    }
}
