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

    private static void Collect(IHardware hardware, HardwareKind kind, List<SensorReading> list)
    {
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
