using TtLcd.Sensors;

namespace TtLcd.Tests;

public class ConfigRulesTests
{
    private static SensorReading S(string id, HardwareKind hw, string name, SensorKind kind, double? value) =>
        new(id, hw.ToString(), hw, name, kind, value);

    private static readonly SensorReading[] Sensors =
    [
        S("/cpu/t", HardwareKind.Cpu, "CPU Package", SensorKind.Temperature, 44),
        S("/cpu/l", HardwareKind.Cpu, "CPU Total", SensorKind.Load, 5),
        S("/gpu/t", HardwareKind.Gpu, "GPU Core", SensorKind.Temperature, 57),
        S("/gpu/l", HardwareKind.Gpu, "GPU Core", SensorKind.Load, 16),
        S("/mb/fan1", HardwareKind.Motherboard, "Fan #1", SensorKind.Fan, 1289),
    ];

    [Fact]
    public void Gpu_source_puts_gpu_sensors_into_the_cpu_fields()
    {
        var config = new AppConfig();
        ConfigRules.SetDisplaySource(config, DisplaySource.Gpu, Sensors);

        Assert.Equal("/gpu/t", config.Sensors[DisplayRole.CpuTemperature]);
        Assert.Equal("/gpu/l", config.Sensors[DisplayRole.CpuUsage]);
        Assert.Equal(DisplaySource.Gpu, ConfigRules.GetDisplaySource(config.Sensors, Sensors));
    }

    [Fact]
    public void Cpu_source_restores_cpu_sensors()
    {
        var config = new AppConfig();
        ConfigRules.SetDisplaySource(config, DisplaySource.Gpu, Sensors);
        ConfigRules.SetDisplaySource(config, DisplaySource.Cpu, Sensors);

        Assert.Equal("/cpu/t", config.Sensors[DisplayRole.CpuTemperature]);
        Assert.Equal(DisplaySource.Cpu, ConfigRules.GetDisplaySource(config.Sensors, Sensors));
    }

    [Fact]
    public void Gpu_source_without_gpu_switches_fields_off()
    {
        var config = new AppConfig();
        ConfigRules.SetDisplaySource(config, DisplaySource.Gpu, Sensors.Where(s => s.HardwareKind != HardwareKind.Gpu).ToList());

        Assert.Equal(SensorSelector.None, config.Sensors[DisplayRole.CpuTemperature]);
    }

    [Fact]
    public void Validate_accepts_known_sensors_and_none()
    {
        var config = new AppConfig();
        config.Sensors[DisplayRole.FanSpeed] = "/mb/fan1";
        config.Sensors[DisplayRole.GpuClock] = "none";

        Assert.Null(ConfigRules.Validate(config, Sensors));
    }

    [Theory]
    [InlineData(100, 250)]   // sensor interval too short
    [InlineData(1000, 2000)] // send interval too long: the LCD would switch off
    public void Validate_rejects_out_of_range_intervals(int sensorMs, int sendMs)
    {
        var config = new AppConfig { SensorIntervalMs = sensorMs, SendIntervalMs = sendMs };

        Assert.NotNull(ConfigRules.Validate(config, Sensors));
    }

    [Fact]
    public void Validate_rejects_unknown_sensor()
    {
        var config = new AppConfig();
        config.Sensors[DisplayRole.FanSpeed] = "/does/not/exist";

        Assert.NotNull(ConfigRules.Validate(config, Sensors));
    }
}
