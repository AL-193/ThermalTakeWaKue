namespace TtLcd;

internal sealed record RunOptions(AppConfig Config, string ConfigPath);

internal static class Runner
{
    /// <summary>Runs the LCD loop together with the connection for the settings app.</summary>
    public static async Task RunAsync(RunOptions options, CancellationToken cancellationToken)
    {
        var service = new LcdService(options.Config);
        using var stopServer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var server = Task.Run(() => new ConfigServer(service, options.ConfigPath).RunAsync(stopServer.Token), CancellationToken.None);
        try
        {
            await service.RunAsync(cancellationToken);
        }
        finally
        {
            stopServer.Cancel();
            await server;
        }
    }
}
