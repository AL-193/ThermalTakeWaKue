namespace TtLcd.Sensors;

public interface ISensorSource : IDisposable
{
    IReadOnlyList<SensorReading> Read();

    /// <summary>
    /// Lets later reads skip hardware that none of these sensors belongs to, where that
    /// saves time. Skipped sensors keep their last value. Null reads everything again.
    /// </summary>
    void Focus(IReadOnlySet<string>? sensorIds)
    {
    }
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
