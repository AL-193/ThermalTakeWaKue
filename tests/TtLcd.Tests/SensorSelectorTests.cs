using TtLcd.Sensors;

namespace TtLcd.Tests;

public class SensorSelectorTests
{
    private static SensorReading S(string id, HardwareKind hw, string name, SensorKind kind, double? value) =>
        new(id, hw.ToString(), hw, name, kind, value);

    private static readonly SensorReading[] Sensors =
    [
        S("/cpu/t/core0", HardwareKind.Cpu, "Core #1", SensorKind.Temperature, 50),
        S("/cpu/t/pkg", HardwareKind.Cpu, "CPU Package", SensorKind.Temperature, 55),
        S("/cpu/c/bus", HardwareKind.Cpu, "Bus Speed", SensorKind.Clock, 100),
        S("/cpu/c/1", HardwareKind.Cpu, "P-Core #1", SensorKind.Clock, 5500),
        S("/cpu/c/2", HardwareKind.Cpu, "P-Core #2", SensorKind.Clock, 5700),
        S("/gpu/fan", HardwareKind.Gpu, "GPU Fan", SensorKind.Fan, 900),
        S("/mb/fan1", HardwareKind.Motherboard, "Fan #1", SensorKind.Fan, 0),
        S("/mb/fan2", HardwareKind.Motherboard, "Fan #2", SensorKind.Fan, 1350),
        S("/mb/fan3", HardwareKind.Motherboard, "AIO Pump", SensorKind.Fan, 2800),
    ];

    [Fact]
    public void Prefers_package_temperature()
    {
        Assert.Equal("/cpu/t/pkg", SensorSelector.AutoSelect(DisplayRole.CpuTemperature, Sensors)?.Id);
    }

    [Fact]
    public void Cpu_clock_is_fastest_core_not_bus()
    {
        Assert.Equal("/cpu/c/2", SensorSelector.AutoSelect(DisplayRole.CpuClock, Sensors)?.Id);
    }

    [Fact]
    public void Fan_skips_gpu_stopped_and_pump_headers()
    {
        Assert.Equal("/mb/fan2", SensorSelector.AutoSelect(DisplayRole.FanSpeed, Sensors)?.Id);
        Assert.Equal("/mb/fan3", SensorSelector.AutoSelect(DisplayRole.PumpSpeed, Sensors)?.Id);
    }

    [Fact]
    public void Configured_sensor_wins_and_missing_one_is_not_replaced()
    {
        Assert.Equal("/cpu/t/core0", SensorSelector.Resolve(DisplayRole.CpuTemperature, Sensors, "/cpu/t/core0")?.Id);
        Assert.Null(SensorSelector.Resolve(DisplayRole.CpuTemperature, Sensors, "/gone"));
    }
}
