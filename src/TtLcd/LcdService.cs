using TtLcd.Device;
using TtLcd.Protocol;
using TtLcd.Sensors;

namespace TtLcd;

/// <summary>Reads sensors and keeps the LCD updated until cancelled.</summary>
public sealed class LcdService(AppConfig config)
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(2);

    private readonly HashSet<DisplayRole> _reportedMissing = [];

    // Written by the sensor loop, read by the send loop.
    private volatile LcdFrame _frame = new();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var sensors = SensorSourceFactory.Create();
        _frame = BuildFrame(sensors.Read());

        // A sensor read can take over 500 ms (LibreHardwareMonitor), while the
        // LCD switches off within a second without packets. Reading on its own
        // thread keeps the send cadence steady.
        using var stopSensors = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var sensorLoop = Task.Run(() => ReadSensorsAsync(sensors, stopSensors.Token), CancellationToken.None);
        try
        {
            await SendLoopAsync(cancellationToken);
        }
        finally
        {
            stopSensors.Cancel();
            await sensorLoop;
        }
    }

    private async Task ReadSensorsAsync(ISensorSource sensors, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(100, config.SensorIntervalMs)));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
                _frame = BuildFrame(sensors.Read());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task SendLoopAsync(CancellationToken cancellationToken)
    {
        using var lcd = new LcdDevice();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(50, config.SendIntervalMs)));

        long nextConnectAttempt = 0;
        bool waitingLogged = false;

        try
        {
            do
            {
                var now = Environment.TickCount64;
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

                if (!lcd.Send(LcdPacket.Update(_frame with { Time = DateTime.Now }, config.TemperatureUnit)))
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
