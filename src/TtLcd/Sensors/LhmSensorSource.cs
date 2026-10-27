using System.Runtime.Versioning;
using LibreHardwareMonitor.Hardware;

namespace TtLcd.Sensors;

/// <summary>
/// Windows sensors via LibreHardwareMonitorLib. CPU temperatures and
/// mainboard fans need administrator rights and the PawnIO driver.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LhmSensorSource : ISensorSource
{
    private readonly Computer _computer;

    // Hardware to update; null = all. Updating e.g. an Intel CPU via PawnIO takes ~350 ms
    // and an ASUS embedded controller up to ~330 ms, so skipping unused hardware matters.
    private HashSet<IHardware>? _focus;

    public LhmSensorSource()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsMemoryEnabled = true,
            IsControllerEnabled = true,
        };
        _computer.Open();
    }

    public IReadOnlyList<SensorReading> Read()
    {
        var list = new List<SensorReading>();
        foreach (var hardware in _computer.Hardware)
            Collect(hardware, Classify(hardware.HardwareType), list);
        return list;
    }

    public void Focus(IReadOnlySet<string>? sensorIds)
    {
        _focus = sensorIds?.Count > 0
            ? _computer.Hardware
                .SelectMany(h => h.SubHardware.Prepend(h))
                .SelectMany(h => h.Sensors)
                .Where(s => sensorIds.Contains(s.Identifier.ToString()))
                .Select(s => s.Hardware)
                .ToHashSet()
            : null;
    }

    private void Collect(IHardware hardware, HardwareKind kind, List<SensorReading> list)
    {
        if (_focus is null || _focus.Contains(hardware))
            hardware.Update();
        foreach (var sensor in hardware.Sensors)
        {
            list.Add(new SensorReading(
                sensor.Identifier.ToString(),
                hardware.Name,
                kind,
                sensor.Name,
                Classify(sensor.SensorType),
                sensor.Value));
        }

        // Sub-hardware (e.g. the Super I/O chip of a mainboard) inherits the parent's kind.
        foreach (var sub in hardware.SubHardware)
            Collect(sub, kind, list);
    }

    private static HardwareKind Classify(HardwareType type) => type switch
    {
        HardwareType.Cpu => HardwareKind.Cpu,
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => HardwareKind.Gpu,
        HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.EmbeddedController => HardwareKind.Motherboard,
        HardwareType.Memory => HardwareKind.Memory,
        HardwareType.Cooler => HardwareKind.Cooler,
        HardwareType.Storage => HardwareKind.Storage,
        _ => HardwareKind.Other,
    };

    private static SensorKind Classify(SensorType type) => type switch
    {
        SensorType.Temperature => SensorKind.Temperature,
        SensorType.Load => SensorKind.Load,
        SensorType.Power => SensorKind.Power,
        SensorType.Clock => SensorKind.Clock,
        SensorType.Voltage => SensorKind.Voltage,
        SensorType.Fan => SensorKind.Fan,
        _ => SensorKind.Other,
    };

    public void Dispose() => _computer.Close();
}
