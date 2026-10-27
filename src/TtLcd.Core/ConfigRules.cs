using TtLcd.Sensors;

namespace TtLcd;

/// <summary>What the LCD's temperature and usage fields show.</summary>
public enum DisplaySource
{
    Cpu,
    Gpu,
}

public static class ConfigRules
{
    public const int MinSensorIntervalMs = 250;
    public const int MaxSensorIntervalMs = 10_000;

    // The LCD switches itself off when packets stop for less than a second.
    public const int MinSendIntervalMs = 100;
    public const int MaxSendIntervalMs = 500;

    /// <summary>Returns an error message, or null when the config can be applied.</summary>
    public static string? Validate(AppConfig config, IReadOnlyList<SensorReading> sensors)
    {
        if (!Enum.IsDefined(config.TemperatureUnit))
            return "Unknown temperature unit.";
        if (config.SensorIntervalMs is < MinSensorIntervalMs or > MaxSensorIntervalMs)
            return $"SensorIntervalMs must be between {MinSensorIntervalMs} and {MaxSensorIntervalMs}.";
        if (config.SendIntervalMs is < MinSendIntervalMs or > MaxSendIntervalMs)
            return $"SendIntervalMs must be between {MinSendIntervalMs} and {MaxSendIntervalMs}.";

        var known = sensors.Select(s => s.Id).ToHashSet();
        foreach (var (role, id) in config.Sensors)
        {
            if (!Enum.IsDefined(role))
                return $"Unknown field '{role}'.";
            if (!SensorSelector.IsNone(id) && !known.Contains(id))
                return $"Unknown sensor for {role}: {id}";
        }
        return null;
    }

    /// <summary>
    /// The LA series LCD only has CPU temperature and usage fields. To show the GPU,
    /// those fields get the GPU's sensors.
    /// </summary>
    public static void SetDisplaySource(AppConfig config, DisplaySource source, IReadOnlyList<SensorReading> sensors)
    {
        var (temperature, usage) = source == DisplaySource.Gpu
            ? (DisplayRole.GpuTemperature, DisplayRole.GpuUsage)
            : (DisplayRole.CpuTemperature, DisplayRole.CpuUsage);
        config.Sensors[DisplayRole.CpuTemperature] = SensorSelector.AutoSelect(temperature, sensors)?.Id ?? SensorSelector.None;
        config.Sensors[DisplayRole.CpuUsage] = SensorSelector.AutoSelect(usage, sensors)?.Id ?? SensorSelector.None;
    }

    public static DisplaySource GetDisplaySource(IReadOnlyDictionary<DisplayRole, string> selection, IReadOnlyList<SensorReading> sensors) =>
        selection.TryGetValue(DisplayRole.CpuTemperature, out var id)
        && sensors.FirstOrDefault(s => s.Id == id)?.HardwareKind == HardwareKind.Gpu
            ? DisplaySource.Gpu
            : DisplaySource.Cpu;
}
