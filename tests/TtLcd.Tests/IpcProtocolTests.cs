using TtLcd.Ipc;
using TtLcd.Protocol;
using TtLcd.Sensors;

namespace TtLcd.Tests;

public class IpcProtocolTests
{
    [Fact]
    public async Task Response_round_trips_including_NaN_values()
    {
        var config = new AppConfig { TemperatureUnit = TemperatureUnit.Fahrenheit };
        config.Sensors[DisplayRole.FanSpeed] = "/mb/fan1";
        var response = new IpcResponse(
            true,
            Config: config,
            Sensors: [new SensorReading("/x", "Board", HardwareKind.Motherboard, "Fan", SensorKind.Fan, double.NaN)],
            Selection: new Dictionary<DisplayRole, string> { [DisplayRole.FanSpeed] = "/mb/fan1" });

        using var stream = new MemoryStream();
        await IpcProtocol.WriteAsync(stream, response, CancellationToken.None);
        stream.Position = 0;
        var read = await IpcProtocol.ReadAsync<IpcResponse>(stream, CancellationToken.None);

        Assert.True(read.Ok);
        Assert.Equal(TemperatureUnit.Fahrenheit, read.Config!.TemperatureUnit);
        Assert.Equal("/mb/fan1", read.Config.Sensors[DisplayRole.FanSpeed]);
        Assert.True(double.IsNaN(read.Sensors![0].Value!.Value));
        Assert.Equal("/mb/fan1", read.Selection![DisplayRole.FanSpeed]);
    }

    [Fact]
    public async Task Reads_one_message_per_line()
    {
        using var stream = new MemoryStream();
        await IpcProtocol.WriteAsync(stream, new IpcRequest(IpcCommands.GetState, AllSensors: true), CancellationToken.None);
        stream.Position = 0;

        var request = await IpcProtocol.ReadAsync<IpcRequest>(stream, CancellationToken.None);

        Assert.Equal(IpcCommands.GetState, request.Command);
        Assert.True(request.AllSensors);
    }

    [Fact]
    public async Task Empty_stream_is_an_error()
    {
        using var stream = new MemoryStream();

        await Assert.ThrowsAsync<EndOfStreamException>(() => IpcProtocol.ReadAsync<IpcRequest>(stream, CancellationToken.None));
    }
}
