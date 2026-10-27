namespace TtLcd.Sensors;

/// <summary>A value field on the LCD.</summary>
public enum DisplayRole
{
    CpuTemperature,
    CpuUsage,
    CpuPower,
    CpuClock,
    CpuVoltage,
    GpuTemperature,
    GpuUsage,
    GpuPower,
    GpuClock,
    FanSpeed,
    PumpSpeed,
    RamUsage,
}

/// <summary>Picks the sensor for each LCD field: the configured one, or a sensible default.</summary>
public static class SensorSelector
{
    /// <summary>Config value that switches a field off (sent as 0, never read).</summary>
    public const string None = "none";

    private static readonly string[] PumpNames = ["pump", "aio"];

    public static bool IsNone(string? configuredId) =>
        string.Equals(configuredId, None, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A configured ID that no longer exists yields null rather than a
    /// different sensor, so the display never silently shows the wrong value.
    /// </summary>
    public static SensorReading? Resolve(DisplayRole role, IReadOnlyList<SensorReading> sensors, string? configuredId)
    {
        if (IsNone(configuredId))
            return null;
        return string.IsNullOrEmpty(configuredId)
            ? AutoSelect(role, sensors)
            : sensors.FirstOrDefault(s => s.Id == configuredId);
    }

    public static SensorReading? AutoSelect(DisplayRole role, IReadOnlyList<SensorReading> sensors) => role switch
    {
        DisplayRole.CpuTemperature =>
            ByName(sensors, HardwareKind.Cpu, SensorKind.Temperature, "CPU Package", "Core (Tctl/Tdie)", "Package id 0", "Tctl", "Tdie")
            ?? Any(sensors, HardwareKind.Cpu, SensorKind.Temperature),
        DisplayRole.CpuUsage =>
            ByName(sensors, HardwareKind.Cpu, SensorKind.Load, "CPU Total"),
        DisplayRole.CpuPower =>
            ByName(sensors, HardwareKind.Cpu, SensorKind.Power, "CPU Package", "Package"),
        DisplayRole.CpuClock =>
            ByName(sensors, HardwareKind.Cpu, SensorKind.Clock, "Core Clock (max)")
            ?? Of(sensors, HardwareKind.Cpu, SensorKind.Clock)
                .Where(s => !s.Name.Contains("Bus", StringComparison.OrdinalIgnoreCase))
                .MaxBy(s => s.Value ?? 0),
        DisplayRole.CpuVoltage =>
            ByName(sensors, HardwareKind.Cpu, SensorKind.Voltage, "CPU Core", "Core (SVI2 TFN)", "Core VID")
            ?? ByName(sensors, HardwareKind.Motherboard, SensorKind.Voltage, "Vcore", "CPU Core"),
        DisplayRole.GpuTemperature =>
            ByName(sensors, HardwareKind.Gpu, SensorKind.Temperature, "GPU Core", "edge")
            ?? Any(sensors, HardwareKind.Gpu, SensorKind.Temperature),
        DisplayRole.GpuUsage =>
            ByName(sensors, HardwareKind.Gpu, SensorKind.Load, "GPU Core"),
        DisplayRole.GpuPower =>
            ByName(sensors, HardwareKind.Gpu, SensorKind.Power, "GPU Package", "GPU Power", "PPT")
            ?? Any(sensors, HardwareKind.Gpu, SensorKind.Power),
        DisplayRole.GpuClock =>
            ByName(sensors, HardwareKind.Gpu, SensorKind.Clock, "GPU Core", "sclk")
            ?? Any(sensors, HardwareKind.Gpu, SensorKind.Clock),
        DisplayRole.FanSpeed =>
            SpinningFans(sensors).FirstOrDefault(s => !IsPump(s)),
        DisplayRole.PumpSpeed =>
            SpinningFans(sensors).FirstOrDefault(IsPump) ?? SpinningFans(sensors).FirstOrDefault(),
        DisplayRole.RamUsage =>
            Of(sensors, HardwareKind.Memory, SensorKind.Load)
                .FirstOrDefault(s => s.Name == "Memory" && !s.Hardware.Contains("Virtual", StringComparison.OrdinalIgnoreCase)),
        _ => null,
    };

    private static IEnumerable<SensorReading> Of(IReadOnlyList<SensorReading> sensors, HardwareKind hardware, SensorKind kind) =>
        sensors.Where(s => s.HardwareKind == hardware && s.Kind == kind);

    private static SensorReading? Any(IReadOnlyList<SensorReading> sensors, HardwareKind hardware, SensorKind kind) =>
        Of(sensors, hardware, kind).FirstOrDefault(s => s.Value is not null);

    /// <summary>First sensor whose name matches, trying the names in order of preference.</summary>
    private static SensorReading? ByName(IReadOnlyList<SensorReading> sensors, HardwareKind hardware, SensorKind kind, params string[] names)
    {
        var candidates = Of(sensors, hardware, kind).ToList();
        foreach (var name in names)
        {
            var match = candidates.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }
        return null;
    }

    private static IEnumerable<SensorReading> SpinningFans(IReadOnlyList<SensorReading> sensors) =>
        sensors.Where(s => s.Kind == SensorKind.Fan && s.HardwareKind != HardwareKind.Gpu && s.Value > 0);

    private static bool IsPump(SensorReading s) =>
        PumpNames.Any(p => s.Name.Contains(p, StringComparison.OrdinalIgnoreCase));
}
