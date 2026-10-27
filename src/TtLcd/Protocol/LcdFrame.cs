namespace TtLcd.Protocol;

public enum TemperatureUnit
{
    Celsius,
    Fahrenheit,
}

/// <summary>
/// One set of values for the segment LCD. Temperatures are always in °C;
/// conversion to °F happens while encoding. A null value is sent as 0.
/// </summary>
public sealed record LcdFrame
{
    public double? CpuTemperature { get; init; }
    public double? CpuUsage { get; init; }
    public double? CpuPower { get; init; }
    public double? CpuClock { get; init; }
    public double? CpuVoltage { get; init; }

    public double? GpuTemperature { get; init; }
    public double? GpuUsage { get; init; }
    public double? GpuPower { get; init; }
    public double? GpuClock { get; init; }

    public double? FanSpeed { get; init; }
    public double? PumpSpeed { get; init; }
    public double? RamUsage { get; init; }

    public DateTime Time { get; init; } = DateTime.Now;
}
