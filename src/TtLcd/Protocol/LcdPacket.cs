namespace TtLcd.Protocol;

/// <summary>
/// Builds the 64-byte HID output reports understood by the LCD.
/// See docs/PROTOCOL.md for the byte layout.
/// </summary>
public static class LcdPacket
{
    public const int Length = 64;

    private const byte CommandUpdate = 0x01;
    private const byte UpdateSubCommand = 0x02;
    private const byte CommandOff = 0x0F;
    private const int PayloadOffset = 3;

    public static byte[] Update(LcdFrame frame, TemperatureUnit unit)
    {
        var p = new byte[Length];
        p[1] = CommandUpdate;
        p[2] = UpdateSubCommand;
        var v = p.AsSpan(PayloadOffset);

        byte unitFlag = unit == TemperatureUnit.Fahrenheit ? (byte)1 : (byte)0;

        var cpuTemp = ToUnit(frame.CpuTemperature, unit);
        v[0] = Whole(cpuTemp);
        v[1] = Fraction(cpuTemp);
        v[2] = unitFlag;
        v[3] = Whole(frame.CpuUsage);
        v[4] = Lower(frame.CpuPower);
        v[5] = Fraction(frame.CpuPower);
        v[6] = Upper(frame.CpuClock);
        v[7] = Lower(frame.CpuClock);
        v[8] = Whole(frame.CpuVoltage);
        v[9] = Fraction(frame.CpuVoltage);

        var gpuTemp = ToUnit(frame.GpuTemperature, unit);
        v[10] = Whole(gpuTemp);
        v[11] = Fraction(gpuTemp);
        v[12] = unitFlag;
        v[13] = Whole(frame.GpuUsage);
        v[14] = Lower(frame.GpuPower);
        v[15] = Fraction(frame.GpuPower);
        v[16] = Upper(frame.GpuClock);
        v[17] = Lower(frame.GpuClock);

        v[18] = Upper(frame.FanSpeed);
        v[19] = Lower(frame.FanSpeed);
        v[20] = Upper(frame.PumpSpeed);
        v[21] = Lower(frame.PumpSpeed);

        var t = frame.Time;
        v[22] = (byte)(t.Year / 100);
        v[23] = (byte)(t.Year % 100);
        v[24] = (byte)t.Month;
        v[25] = (byte)t.Day;
        v[26] = (byte)t.Hour;
        v[27] = (byte)t.Minute;
        v[28] = (byte)t.Second;
        v[29] = (byte)t.DayOfWeek; // Sunday = 0

        v[30] = Whole(frame.RamUsage);
        v[31] = Upper(frame.CpuPower);
        v[32] = Upper(frame.GpuPower);
        return p;
    }

    /// <summary>Sent by the original software on exit; blanks the display.</summary>
    public static byte[] Off()
    {
        var p = new byte[Length];
        p[1] = CommandOff;
        return p;
    }

    private static double? ToUnit(double? celsius, TemperatureUnit unit) =>
        unit == TemperatureUnit.Fahrenheit && celsius is { } c ? c * 9 / 5 + 32 : celsius;

    private static int Floor(double? value) =>
        value is { } v && double.IsFinite(v) && v > 0 ? (int)Math.Min(Math.Floor(v), 9999) : 0;

    /// <summary>Integer part, 0..255.</summary>
    private static byte Whole(double? value) => (byte)Math.Min(Floor(value), 255);

    /// <summary>Two decimal places, 0..99.</summary>
    private static byte Fraction(double? value)
    {
        if (value is not { } v || !double.IsFinite(v) || v <= 0)
            return 0;
        return (byte)Math.Min((int)Math.Round((v - Math.Floor(v)) * 100), 99);
    }

    /// <summary>Hundreds of a 0..9999 value (e.g. 12 for 1234 RPM).</summary>
    private static byte Upper(double? value) => (byte)(Floor(value) / 100);

    /// <summary>Last two digits of a 0..9999 value (e.g. 34 for 1234 RPM).</summary>
    private static byte Lower(double? value) => (byte)(Floor(value) % 100);
}
