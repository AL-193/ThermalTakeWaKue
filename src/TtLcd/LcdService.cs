using TtLcd.Device;
using TtLcd.Protocol;
using TtLcd.Sensors;

namespace TtLcd;

/// <summary>Reads sensors and keeps the LCD updated until cancelled.</summary>
public sealed class LcdService(AppConfig config)
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(2);

    private readonly HashSet<DisplayRole> _reportedMissing = [];

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var sensors = SensorSourceFactory.Create();
        using var lcd = new LcdDevice();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(50, config.SendIntervalMs)));

        var frame = new LcdFrame();
        long nextSensorRead = 0;
        long nextConnectAttempt = 0;
        bool waitingLogged = false;

        try
        {
            do
            {
                var now = Environment.TickCount64;
                if (now >= nextSensorRead)
                {
                    frame = BuildFrame(sensors.Read());
                    nextSensorRead = now + config.SensorIntervalMs;
                }

                if (!lcd.IsConnected)
                {
                    if (now < nextConnectAttempt)
                        continue;
                    if (!lcd.TryConnect())
                    {
                        if (!waitingLogged)
                            Log.Info($"Waiting for LCD (USB {LcdDevice.VendorId:X4}:{LcdDevice.ProductId:X4}) ...");
                        waitingLogged = true;
                        nextConnectAttempt = now + (long)ReconnectDelay.TotalMilliseconds;
                        continue;
                    }
                    Log.Info($"LCD connected: {lcd.DevicePath}");
                    waitingLogged = false;
                }

                if (!lcd.Send(LcdPacket.Update(frame with { Time = DateTime.Now }, config.TemperatureUnit)))
                    Log.Warn("LCD disconnected.");
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (lcd.Send(LcdPacket.Off()))
                Log.Info("LCD switched off.");
        }
    }

    private LcdFrame BuildFrame(IReadOnlyList<SensorReading> sensors)
    {
        double? Value(DisplayRole role)
        {
            config.Sensors.TryGetValue(role, out var id);
            var sensor = SensorSelector.Resolve(role, sensors, id);
            if (sensor is null && id is not null && _reportedMissing.Add(role))
                Log.Warn($"Configured sensor for {role} not found: {id}");
            return sensor?.Value;
        }

        return new LcdFrame
        {
            CpuTemperature = Value(DisplayRole.CpuTemperature),
            CpuUsage = Value(DisplayRole.CpuUsage),
            CpuPower = Value(DisplayRole.CpuPower),
            CpuClock = Value(DisplayRole.CpuClock),
            CpuVoltage = Value(DisplayRole.CpuVoltage),
            GpuTemperature = Value(DisplayRole.GpuTemperature),
            GpuUsage = Value(DisplayRole.GpuUsage),
            GpuPower = Value(DisplayRole.GpuPower),
            GpuClock = Value(DisplayRole.GpuClock),
            FanSpeed = Value(DisplayRole.FanSpeed),
            PumpSpeed = Value(DisplayRole.PumpSpeed),
            RamUsage = Value(DisplayRole.RamUsage),
        };
    }
}
