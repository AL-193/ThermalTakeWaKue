using System.Diagnostics;
using System.Runtime.Versioning;
using System.ServiceProcess;

namespace TtLcd;

/// <summary>Registers ttlcd as an automatically starting Windows service (runs as LocalSystem).</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsService
{
    public const string Name = "ttlcd";
    private const string DisplayName = "Thermaltake LCD (ttlcd)";
    private const string Description = "Shows CPU and fan sensor values on the Thermaltake LA series segment LCD.";

    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ttlcd");

    public static string ConfigPath => Path.Combine(DataDirectory, "config.json");

    public static string LogPath => Path.Combine(DataDirectory, "ttlcd.log");

    private static readonly TimeSpan StateTimeout = TimeSpan.FromSeconds(30);

    public static void Install()
    {
        if (Exists())
            Uninstall();

        Sc("create", Name, "binPath=", CommandLine(), "start=", "auto", "DisplayName=", DisplayName);
        Sc("description", Name, Description);
        // Restart after a crash: 5 s, 5 s, then 30 s; reset the counter after a day.
        Sc("failure", Name, "reset=", "86400", "actions=", "restart/5000/restart/5000/restart/30000");

        using var service = new ServiceController(Name);
        service.Start();
        service.WaitForStatus(ServiceControllerStatus.Running, StateTimeout);
    }

    public static void Uninstall()
    {
        using (var service = new ServiceController(Name))
        {
            if (service.Status != ServiceControllerStatus.Stopped)
            {
                if (service.Status != ServiceControllerStatus.StopPending)
                    service.Stop();
                // Deleting a running service only marks it for deletion and blocks a re-install.
                service.WaitForStatus(ServiceControllerStatus.Stopped, StateTimeout);
            }
        }
        Sc("delete", Name);
    }

    private static bool Exists() =>
        ServiceController.GetServices().Any(s => s.ServiceName.Equals(Name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Re-uses however this process was started. Run through <c>dotnet ttlcd.dll</c>,
    /// the service also starts through the signed dotnet host, which Windows Smart App
    /// Control accepts while ttlcd itself is not code-signed.
    /// </summary>
    private static string CommandLine()
    {
        var process = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown process path.");
        var viaDotnet = Path.GetFileNameWithoutExtension(process).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        var app = viaDotnet
            ? $"\"{process}\" \"{Path.Combine(AppContext.BaseDirectory, "ttlcd.dll")}\""
            : $"\"{process}\"";
        return $"{app} run --config \"{ConfigPath}\"";
    }

    private static void Sc(params string[] args)
    {
        var psi = new ProcessStartInfo("sc.exe") { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new IOException($"sc {args[0]} failed ({process.ExitCode}): {output.Trim()}");
    }
}
