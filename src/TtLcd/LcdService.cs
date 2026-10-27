using TtLcd.Device;
using TtLcd.Protocol;
using TtLcd.Sensors;

namespace TtLcd;

/// <summary>Reads sensors and keeps the LCD updated until cancelled.</summary>
public sealed class LcdService(AppConfig config)
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(2);

    // Sensor ID per LCD field, chosen once at start-up.
    private Dictionary<DisplayRole, string> _selection = [];

    // Written by the sensor loop, read by the send loop.
    private volatile LcdFrame _frame = new();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using ISensorSource sensors = SensorSourceFactory.Create();
        _selection = SelectSensors(sensors.Read());
        sensors.Focus(_selection.Values.ToHashSet());
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

    private Dictionary<DisplayRole, string> SelectSensors(IReadOnlyList<SensorReading> sensors)
    {
        var selection = new Dictionary<DisplayRole, string>();
        foreach (var role in Enum.GetValues<DisplayRole>())
        {
            config.Sensors.TryGetValue(role, out var id);
            var sensor = SensorSelector.Resolve(role, sensors, id);
            if (sensor is not null)
            {
                selection[role] = sensor.Id;
                Log.Info($"{role,-15} {sensor.Hardware} / {sensor.Name} ({sensor.Id})");
            }
            else if (SensorSelector.IsNone(id))
                Log.Info($"{role,-15} off");
            else if (id is not null)
                Log.Warn($"{role,-15} configured sensor not found: {id}");
            else
                Log.Info($"{role,-15} no matching sensor");
        }
        return selection;
    }

    private LcdFrame BuildFrame(IReadOnlyList<SensorReading> sensors)
    {
        var byId = sensors.DistinctBy(s => s.Id).ToDictionary(s => s.Id);

        double? Value(DisplayRole role) =>
            _selection.TryGetValue(role, out var id) && byId.TryGetValue(id, out var sensor) ? sensor.Value : null;

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
