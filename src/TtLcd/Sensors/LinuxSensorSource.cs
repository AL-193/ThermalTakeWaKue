using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace TtLcd.Sensors;

/// <summary>
/// Linux sensors from sysfs/procfs (hwmon, cpufreq, RAPL, /proc/stat,
/// /proc/meminfo) plus NVML for NVIDIA GPUs. RAPL needs root.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed partial class LinuxSensorSource : ISensorSource
{
    private const string HwmonRoot = "/sys/class/hwmon";
    private const string RaplRoot = "/sys/class/powercap/intel-rapl:0";

    private readonly string _cpuName = ReadCpuName();
    private readonly NvmlGpuReader? _nvml = NvmlGpuReader.TryCreate();

    private (ulong Total, ulong Idle)? _lastCpuTimes;
    private (ulong EnergyUj, long Timestamp)? _lastRapl;

    public IReadOnlyList<SensorReading> Read()
    {
        var list = new List<SensorReading>();
        ReadCpuUsage(list);
        ReadCpuClock(list);
        ReadCpuPower(list);
        ReadMemory(list);
        ReadHwmon(list);
        _nvml?.Read(list);
        return list;
    }

    private void ReadCpuUsage(List<SensorReading> list)
    {
        var line = File.ReadLines("/proc/stat").FirstOrDefault(l => l.StartsWith("cpu "));
        if (line is null)
            return;

        // user nice system idle iowait irq softirq steal
        var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Take(8)
            .Select(ulong.Parse).ToArray();
        ulong total = 0;
        foreach (var f in fields)
            total += f;
        ulong idle = fields[3] + (fields.Length > 4 ? fields[4] : 0);

        double? usage = null;
        if (_lastCpuTimes is { } last && total > last.Total)
            usage = 100.0 * (1.0 - (double)(idle - last.Idle) / (total - last.Total));
        _lastCpuTimes = (total, idle);

        list.Add(new("/cpu/load/total", _cpuName, HardwareKind.Cpu, "CPU Total", SensorKind.Load, usage));
    }

    private void ReadCpuClock(List<SensorReading> list)
    {
        var mhz = Directory.EnumerateDirectories("/sys/devices/system/cpu", "cpu*")
            .Select(d => ReadNumber(Path.Combine(d, "cpufreq", "scaling_cur_freq")))
            .OfType<double>()
            .Select(khz => khz / 1000)
            .ToList();
        if (mhz.Count == 0)
            return;

        list.Add(new("/cpu/clock/max", _cpuName, HardwareKind.Cpu, "Core Clock (max)", SensorKind.Clock, mhz.Max()));
        list.Add(new("/cpu/clock/average", _cpuName, HardwareKind.Cpu, "Core Clock (average)", SensorKind.Clock, mhz.Average()));
    }

    private void ReadCpuPower(List<SensorReading> list)
    {
        var energy = ReadNumber(Path.Combine(RaplRoot, "energy_uj"));
        if (energy is null)
            return;

        var now = Stopwatch.GetTimestamp();
        var energyUj = (ulong)energy.Value;
        double? watts = null;
        if (_lastRapl is { } last)
        {
            ulong delta = energyUj >= last.EnergyUj
                ? energyUj - last.EnergyUj
                : energyUj + (ulong)(ReadNumber(Path.Combine(RaplRoot, "max_energy_range_uj")) ?? 0) - last.EnergyUj;
            var seconds = Stopwatch.GetElapsedTime(last.Timestamp, now).TotalSeconds;
            if (seconds > 0)
                watts = delta / 1e6 / seconds;
        }
        _lastRapl = (energyUj, now);

        list.Add(new("/cpu/power/package", _cpuName, HardwareKind.Cpu, "CPU Package", SensorKind.Power, watts));
    }

    private static void ReadMemory(List<SensorReading> list)
    {
        double? total = null, available = null;
        foreach (var line in File.ReadLines("/proc/meminfo"))
        {
            if (line.StartsWith("MemTotal:"))
                total = ParseMeminfo(line);
            else if (line.StartsWith("MemAvailable:"))
                available = ParseMeminfo(line);
        }
        double? load = total > 0 && available is { } a ? 100.0 * (1 - a / total.Value) : null;
        list.Add(new("/memory/load", "Memory", HardwareKind.Memory, "Memory", SensorKind.Load, load));
    }

    private static double? ParseMeminfo(string line) =>
        double.TryParse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1], CultureInfo.InvariantCulture, out var kb) ? kb : null;

    private static void ReadHwmon(List<SensorReading> list)
    {
        if (!Directory.Exists(HwmonRoot))
            return;

        // hwmonN numbering can change between boots; order by chip name and
        // device path instead so the IDs stay stable.
        var chips = Directory.EnumerateDirectories(HwmonRoot)
            .Select(dir => (Dir: dir, Name: ReadText(Path.Combine(dir, "name")) ?? Path.GetFileName(dir), Device: DevicePath(dir)))
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .ThenBy(c => c.Device, StringComparer.Ordinal);

        var seen = new Dictionary<string, int>();
        foreach (var chip in chips)
        {
            int index = seen.GetValueOrDefault(chip.Name);
            seen[chip.Name] = index + 1;
            var prefix = $"/hwmon/{chip.Name}/{index}";
            var kind = ClassifyChip(chip.Name);

            var channels = new Dictionary<string, (SensorKind Kind, double Value, bool Average)>();
            foreach (var file in Directory.EnumerateFiles(chip.Dir))
            {
                var m = HwmonInput().Match(Path.GetFileName(file));
                if (!m.Success || ReadNumber(file) is not { } raw)
                    continue;

                var channel = m.Groups[1].Value + m.Groups[2].Value;
                bool average = m.Groups[3].Value == "average";
                // Prefer powerN_average over powerN_input when a chip offers both.
                if (channels.TryGetValue(channel, out var existing) && existing.Average)
                    continue;

                var (sensorKind, value) = m.Groups[1].Value switch
                {
                    "temp" => (SensorKind.Temperature, raw / 1000),
                    "fan" => (SensorKind.Fan, raw),
                    "in" => (SensorKind.Voltage, raw / 1000),
                    "power" => (SensorKind.Power, raw / 1e6),
                    "freq" => (SensorKind.Clock, raw / 1e6),
                    _ => (SensorKind.Other, raw),
                };
                channels[channel] = (sensorKind, value, average);
            }

            foreach (var (channel, data) in channels.OrderBy(c => c.Key, StringComparer.Ordinal))
            {
                var label = ReadText(Path.Combine(chip.Dir, channel + "_label")) ?? channel;
                list.Add(new($"{prefix}/{channel}", chip.Name, kind, label, data.Kind, data.Value));
            }

            // amdgpu exposes its utilisation next to the hwmon directory.
            if (ReadNumber(Path.Combine(chip.Dir, "device", "gpu_busy_percent")) is { } busy)
                list.Add(new($"{prefix}/gpu_busy_percent", chip.Name, kind, "GPU Core", SensorKind.Load, busy));
        }
    }

    private static HardwareKind ClassifyChip(string name) => name switch
    {
        "coretemp" or "k10temp" or "zenpower" or "cpu_thermal" => HardwareKind.Cpu,
        "amdgpu" or "radeon" or "nouveau" or "i915" or "xe" => HardwareKind.Gpu,
        "nvme" or "drivetemp" => HardwareKind.Storage,
        _ when name.StartsWith("nct") || name.StartsWith("it8") || name.StartsWith("asus")
            || name.StartsWith("f71") || name.StartsWith("w83") => HardwareKind.Motherboard,
        _ => HardwareKind.Other,
    };

    [GeneratedRegex(@"^(temp|fan|in|power|freq)(\d+)_(input|average)$")]
    private static partial Regex HwmonInput();

    private static string DevicePath(string hwmonDir)
    {
        try
        {
            return new DirectoryInfo(Path.Combine(hwmonDir, "device")).ResolveLinkTarget(true)?.FullName ?? "";
        }
        catch (IOException)
        {
            return "";
        }
    }

    private static string ReadCpuName()
    {
        try
        {
            var line = File.ReadLines("/proc/cpuinfo").FirstOrDefault(l => l.StartsWith("model name"));
            return line?.Split(':', 2)[1].Trim() ?? "CPU";
        }
        catch (IOException)
        {
            return "CPU";
        }
    }

    private static string? ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static double? ReadNumber(string path) =>
        double.TryParse(ReadText(path), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    public void Dispose() => _nvml?.Dispose();
}
