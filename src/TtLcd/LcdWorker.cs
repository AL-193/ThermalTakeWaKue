using Microsoft.Extensions.Hosting;

namespace TtLcd;

/// <summary>Runs ttlcd under the Windows service manager or systemd.</summary>
internal sealed class LcdWorker(RunOptions options) : BackgroundService
{
    // Task.Run: opening the sensors takes about a second and must not delay service start-up.
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => Runner.RunAsync(options, stoppingToken), CancellationToken.None);
}
