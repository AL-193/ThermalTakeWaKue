namespace TtLcd.Sensors;

public enum SensorKind
{
    Temperature,
    Load,
    Power,
    Clock,
    Voltage,
    Fan,
    Other,
}

public enum HardwareKind
{
    Cpu,
    Gpu,
    Motherboard,
    Memory,
    Cooler,
    Storage,
    Other,
}

/// <summary>
/// One sensor value. Units: °C, %, W, MHz, V, RPM.
/// <see cref="Id"/> is stable across runs and is what the config file refers to.
/// </summary>
public sealed record SensorReading(
    string Id,
    string Hardware,
    HardwareKind HardwareKind,
    string Name,
    SensorKind Kind,
    double? Value);

public interface ISensorSource : IDisposable
{
    IReadOnlyList<SensorReading> Read();
}

public static class SensorSourceFactory
{
    public static ISensorSource Create()
    {
        if (OperatingSystem.IsWindows())
            return new LhmSensorSource();
        if (OperatingSystem.IsLinux())
            return new LinuxSensorSource();
        throw new PlatformNotSupportedException("Only Windows and Linux are supported.");
    }
}
