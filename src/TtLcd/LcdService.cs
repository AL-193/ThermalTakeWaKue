using TtLcd.Device;
using TtLcd.Protocol;
using TtLcd.Sensors;

namespace TtLcd;

/// <summary>
/// Reads sensors and keeps the LCD updated until cancelled. Settings can be
/// changed while running (<see cref="ApplyConfig"/>); all sensor access happens
/// on the sensor loop.
/// </summary>
public sealed class LcdService(AppConfig initialConfig)
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(2);

    private volatile AppConfig _config = initialConfig;
    private AppConfig? _pendingConfig;
    private TaskCompletionSource<IReadOnlyList<SensorReading>>? _fullReadRequest;

    // Published by the sensor loop; replaced, never modified.
    private volatile IReadOnlyDictionary<DisplayRole, string> _selection = new Dictionary<DisplayRole, string>();
    private volatile IReadOnlyList<SensorReading> _latest = [];
    private volatile LcdFrame _frame = new();

    public AppConfig Config => _config;

    /// <summary>Sensor ID per LCD field.</summary>
    public IReadOnlyDictionary<DisplayRole, string> Selection => _selection;

    /// <summary>Last readings; sensors on hardware the LCD does not use may be stale.</summary>
    public IReadOnlyList<SensorReading> LatestReadings => _latest;

    /// <summary>Takes effect on the next sensor tick.</summary>
    public void ApplyConfig(AppConfig config) => Volatile.Write(ref _pendingConfig, config);

    /// <summary>Reads every sensor on the next sensor tick.</summary>
    public Task<IReadOnlyList<SensorReading>> ReadAllSensorsAsync()
    {
        var request = new TaskCompletionSource<IReadOnlyList<SensorReading>>(TaskCreationOptions.RunContinuationsAsynchronously);
        return (Interlocked.CompareExchange(ref _fullReadRequest, request, null) ?? request).Task;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using ISensorSource sensors = SensorSourceFactory.Create();
        SelectSensors(sensors);

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
        using var timer = new PeriodicTimer(SensorInterval(_config));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (Interlocked.Exchange(ref _pendingConfig, null) is { } config)
                {
                    Log.Info("Settings changed.");
                    _config = config;
                    timer.Period = SensorInterval(config);
                    SelectSensors(sensors);
                }
                else if (Interlocked.Exchange(ref _fullReadRequest, null) is { } request)
                {
                    sensors.Focus(null);
                    var all = sensors.Read();
                    sensors.Focus(_selection.Values.ToHashSet());
                    Publish(all);
                    request.TrySetResult(all);
                }
                else
                {
                    Publish(sensors.Read());
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            Interlocked.Exchange(ref _fullReadRequest, null)?.TrySetCanceled();
        }
    }

    /// <summary>Chooses a sensor per field from a full read, then reads only what those need.</summary>
    private void SelectSensors(ISensorSource sensors)
    {
        sensors.Focus(null);
        var all = sensors.Read();
        _selection = Select(all);
        sensors.Focus(_selection.Values.ToHashSet());
        Publish(all);
    }

    private void Publish(IReadOnlyList<SensorReading> readings)
    {
        _latest = readings;
        _frame = BuildFrame(readings);
    }

    private static TimeSpan SensorInterval(AppConfig config) => TimeSpan.FromMilliseconds(
        Math.Clamp(config.SensorIntervalMs, ConfigRules.MinSensorIntervalMs, ConfigRules.MaxSensorIntervalMs));

    private static TimeSpan SendInterval(AppConfig config) => TimeSpan.FromMilliseconds(
        Math.Clamp(config.SendIntervalMs, ConfigRules.MinSendIntervalMs, ConfigRules.MaxSendIntervalMs));

    private async Task SendLoopAsync(CancellationToken cancellationToken)
    {
        using var lcd = new LcdDevice();
        using var timer = new PeriodicTimer(SendInterval(_config));

        long nextConnectAttempt = 0;
        bool waitingLogged = false;

        try
        {
            do
            {
                var config = _config;
                if (timer.Period != SendInterval(config))
                    timer.Period = SendInterval(config);

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

    private Dictionary<DisplayRole, string> Select(IReadOnlyList<SensorReading> sensors)
    {
        var selection = new Dictionary<DisplayRole, string>();
        foreach (var role in Enum.GetValues<DisplayRole>())
        {
            _config.Sensors.TryGetValue(role, out var id);
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
        var selection = _selection;

        double? Value(DisplayRole role) =>
            selection.TryGetValue(role, out var id) && byId.TryGetValue(id, out var sensor) ? sensor.Value : null;

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
