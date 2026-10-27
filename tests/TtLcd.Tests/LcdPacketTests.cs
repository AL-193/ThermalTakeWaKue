using TtLcd.Protocol;

namespace TtLcd.Tests;

public class LcdPacketTests
{
    private static readonly LcdFrame Sample = new()
    {
        CpuTemperature = 42.5, CpuUsage = 37.9, CpuPower = 123.45, CpuClock = 4567, CpuVoltage = 1.25,
        GpuTemperature = 55.75, GpuUsage = 88, GpuPower = 234.5, GpuClock = 1950,
        FanSpeed = 1234, PumpSpeed = 2345, RamUsage = 61,
        Time = new DateTime(2026, 10, 27, 21, 5, 9), // a Tuesday
    };

    [Fact]
    public void Update_has_header_and_fixed_length()
    {
        var p = LcdPacket.Update(Sample, TemperatureUnit.Celsius);

        Assert.Equal(64, p.Length);
        Assert.Equal(new byte[] { 0x00, 0x01, 0x02 }, p[..3]);
        Assert.All(p[36..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void Update_encodes_all_fields()
    {
        var p = LcdPacket.Update(Sample, TemperatureUnit.Celsius);

        byte[] expected =
        [
            42, 50, 0, 37,   // CPU temp, fraction, unit, usage
            23, 45, 45, 67,  // CPU power low/fraction, clock high/low
            1, 25,           // CPU voltage
            55, 75, 0, 88,   // GPU temp, fraction, unit, usage
            34, 50, 19, 50,  // GPU power low/fraction, clock high/low
            12, 34, 23, 45,  // fan, pump
            20, 26, 10, 27, 21, 5, 9, 2, // date/time, weekday
            61, 1, 2,        // RAM, CPU power high, GPU power high
        ];
        Assert.Equal(expected, p[3..36]);
    }

    [Fact]
    public void Fahrenheit_converts_temperatures_and_sets_flags()
    {
        var p = LcdPacket.Update(Sample with { CpuTemperature = 100, GpuTemperature = 0 }, TemperatureUnit.Fahrenheit);

        Assert.Equal(212, p[3]);
        Assert.Equal(1, p[5]);
        Assert.Equal(32, p[13]);
        Assert.Equal(1, p[15]);
    }

    [Fact]
    public void Missing_and_out_of_range_values_are_clamped()
    {
        var frame = new LcdFrame { CpuTemperature = null, CpuUsage = -5, FanSpeed = 123456, RamUsage = double.NaN, Time = Sample.Time };
        var p = LcdPacket.Update(frame, TemperatureUnit.Celsius);

        Assert.Equal(0, p[3]);
        Assert.Equal(0, p[6]);
        Assert.Equal(99, p[21]); // 9999 RPM max
        Assert.Equal(99, p[22]);
        Assert.Equal(0, p[33]);
    }

    [Fact]
    public void Fraction_never_rounds_up_to_100()
    {
        var p = LcdPacket.Update(Sample with { CpuTemperature = 42.999 }, TemperatureUnit.Celsius);

        Assert.Equal(42, p[3]);
        Assert.Equal(99, p[4]);
    }

    [Fact]
    public void Off_packet()
    {
        var p = LcdPacket.Off();

        Assert.Equal(64, p.Length);
        Assert.Equal(0x0F, p[1]);
        Assert.Equal(63, p.Count(b => b == 0));
    }
}
