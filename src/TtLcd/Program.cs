using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Systemd;
using Microsoft.Extensions.Hosting.WindowsServices;
using TtLcd;
using TtLcd.Device;
using TtLcd.Protocol;
using TtLcd.Sensors;

const string Usage = """
    ttlcd - driver for the Thermaltake segment LCD (LA series water blocks)

    Usage: ttlcd [command] [--config <path>]

    Commands:
      run           Keep the LCD updated with sensor values (default)
      list          Show all sensors and which one each LCD field uses
      init-config   Write a config file with the current sensor choices
      demo          Show fixed test values on the LCD
      off           Switch the LCD off

    Windows (as administrator):
      install-service     Run ttlcd as a Windows service that starts at boot
      uninstall-service   Remove the service

    Default config: {0}
    """;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var command = args.FirstOrDefault(a => !a.StartsWith('-')) ?? "run";
var configIndex = Array.IndexOf(args, "--config");
var configPath = configIndex >= 0 && configIndex + 1 < args.Length ? args[configIndex + 1] : AppConfig.DefaultPath;

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine(Usage, AppConfig.DefaultPath);
    return 0;
}

try
{
    return command switch
    {
        "run" => await Run(),
        "list" => List(),
        "init-config" => InitConfig(),
        "demo" => await Demo(),
        "off" => Off(),
        "install-service" => InstallService(),
        "uninstall-service" => UninstallService(),
        _ => Fail($"Unknown command '{command}'. Use --help."),
    };
}
catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException
    or InvalidOperationException or System.ComponentModel.Win32Exception)
{
    return Fail(e.Message);
}

async Task<int> Run()
{
    bool asService = WindowsServiceHelpers.IsWindowsService() || SystemdHelpers.IsSystemdService();
    if (asService && OperatingSystem.IsWindows())
        Log.ToFile(WindowsService.LogPath);

    var config = AppConfig.Load(configPath);
    Log.Info($"Config: {(File.Exists(configPath) ? configPath : "defaults (no file at " + configPath + ")")}");
    WarnIfUnprivileged();

    if (asService)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddWindowsService(o => o.ServiceName = "ttlcd");
        builder.Services.AddSystemd();
        builder.Services.AddSingleton(config);
        builder.Services.AddHostedService<LcdWorker>();
        await builder.Build().RunAsync();
        return 0;
    }

    using var cts = CancelOnShutdown();
    await new LcdService(config).RunAsync(cts.Token);
    return 0;
}

int InstallService()
{
    if (!OperatingSystem.IsWindows())
        return Fail("On Linux, use packaging/linux/ttlcd.service (see README).");
    if (!Environment.IsPrivilegedProcess)
        return Fail("Run this command as administrator.");

    if (!File.Exists(WindowsService.ConfigPath))
        WriteAutoConfig(WindowsService.ConfigPath);
    WindowsService.Install();
    Console.WriteLine($"Service '{WindowsService.Name}' installed and started.");
    Console.WriteLine($"  Config: {WindowsService.ConfigPath}  (restart the service after editing)");
    Console.WriteLine($"  Log:    {WindowsService.LogPath}");
    return 0;
}

int UninstallService()
{
    if (!OperatingSystem.IsWindows())
        return Fail("Only available on Windows.");
    if (!Environment.IsPrivilegedProcess)
        return Fail("Run this command as administrator.");

    WindowsService.Uninstall();
    Console.WriteLine($"Service '{WindowsService.Name}' removed.");
    return 0;
}

int List()
{
    WarnIfUnprivileged();
    var config = AppConfig.Load(configPath);
    using var source = SensorSourceFactory.Create();
    source.Read(); // first read primes rate-based sensors (CPU load, power)
    Thread.Sleep(1000);
    var sensors = source.Read();

    foreach (var group in sensors.GroupBy(s => (s.HardwareKind, s.Hardware)))
    {
        Console.WriteLine($"\n[{group.Key.HardwareKind}] {group.Key.Hardware}");
        foreach (var s in group)
            Console.WriteLine($"  {s.Kind,-12} {s.Name,-28} {Format(s.Value),10}   {s.Id}");
    }

    Console.WriteLine("\nLCD fields:");
    foreach (var role in Enum.GetValues<DisplayRole>())
    {
        config.Sensors.TryGetValue(role, out var id);
        var s = SensorSelector.Resolve(role, sensors, id);
        var how = id is null ? "auto" : "config";
        Console.WriteLine(s is null
            ? $"  {role,-15} -- none ({how})"
            : $"  {role,-15} {Format(s.Value),10}  {s.Hardware} / {s.Name}  ({how}: {s.Id})");
    }
    return 0;
}

int InitConfig()
{
    WriteAutoConfig(configPath);
    return 0;
}

static void WriteAutoConfig(string path)
{
    var config = AppConfig.Load(path);
    using var source = SensorSourceFactory.Create();
    source.Read();
    Thread.Sleep(1000);
    var sensors = source.Read();
    foreach (var role in Enum.GetValues<DisplayRole>())
    {
        if (!config.Sensors.ContainsKey(role) && SensorSelector.AutoSelect(role, sensors) is { } s)
            config.Sensors[role] = s.Id;
    }
    config.Save(path);
    Console.WriteLine($"Wrote {path}");
}

async Task<int> Demo()
{
    using var lcd = new LcdDevice();
    if (!lcd.TryConnect())
        return Fail("LCD not found.");

    var frame = new LcdFrame
    {
        CpuTemperature = 42.5, CpuUsage = 37, CpuPower = 123.45, CpuClock = 4567, CpuVoltage = 1.25,
        GpuTemperature = 55.75, GpuUsage = 88, GpuPower = 234.5, GpuClock = 1950,
        FanSpeed = 1234, PumpSpeed = 2345, RamUsage = 61,
    };
    Console.WriteLine("Showing test values for 30 s (Ctrl+C to stop):");
    Console.WriteLine("  CPU 42.5°C 37% 123.45W 4567MHz 1.25V | GPU 55.75°C 88% 234.5W 1950MHz | Fan 1234 | Pump 2345 | RAM 61%");

    using var cts = CancelOnShutdown();
    cts.CancelAfter(TimeSpan.FromSeconds(30));
    try
    {
        while (!cts.IsCancellationRequested)
        {
            lcd.Send(LcdPacket.Update(frame with { Time = DateTime.Now }, TemperatureUnit.Celsius));
            await Task.Delay(250, cts.Token);
        }
    }
    catch (OperationCanceledException)
    {
    }
    lcd.Send(LcdPacket.Off());
    return 0;
}

int Off()
{
    using var lcd = new LcdDevice();
    if (!lcd.TryConnect() || !lcd.Send(LcdPacket.Off()))
        return Fail("LCD not found.");
    return 0;
}

static CancellationTokenSource CancelOnShutdown()
{
    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };
    // Covers systemd stop / kill on Linux; on Windows SIGTERM maps to console close events.
    PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx =>
    {
        ctx.Cancel = true;
        cts.Cancel();
    });
    return cts;
}

static void WarnIfUnprivileged()
{
    if (Environment.IsPrivilegedProcess)
        return;
    Log.Warn(OperatingSystem.IsWindows()
        ? "Not running as administrator: CPU temperature, CPU power and mainboard fans will be missing."
        : "Not running as root: CPU power (RAPL) may be missing.");
}

static string Format(double? value) => value is { } v ? v.ToString("0.##") : "-";

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}
