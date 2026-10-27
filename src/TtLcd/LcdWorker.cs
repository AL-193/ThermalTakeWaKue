using Microsoft.Extensions.Hosting;

namespace TtLcd;

/// <summary>Runs <see cref="LcdService"/> under the Windows service manager or systemd.</summary>
internal sealed class LcdWorker(AppConfig config) : BackgroundService
{
    // Task.Run: opening the sensors takes about a second and must not delay service start-up.
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => new LcdService(config).RunAsync(stoppingToken), CancellationToken.None);
}
