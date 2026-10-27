using System.Text.Json;
using TtLcd.Ipc;
using TtLcd.Protocol;
using TtLcd.Sensors;

namespace TtLcd;

/// <summary>
/// <c>ttlcd configure</c>: the settings of ttlcd-settings as a console menu. Talks to the
/// running ttlcd over the same local connection, so it needs no administrator rights.
/// </summary>
internal static class ConfigureCommand
{
    private static readonly int[] IntervalChoices = [500, 1000, 2000];

    public static async Task<int> RunAsync()
    {
        var client = new ConfigClient();
        IpcResponse state;
        try
        {
            Console.WriteLine("Connecting to the ttlcd service ...");
            state = await client.GetStateAsync(allSensors: true);
        }
        catch (Exception e) when (IsConnectionError(e))
        {
            Console.Error.WriteLine("The ttlcd service is not reachable. Is it installed and running?");
            return 1;
        }
        if (!state.Ok || state.Config is null || state.Sensors is null || state.Selection is null)
        {
            Console.Error.WriteLine(state.Error ?? "Unexpected answer from the service.");
            return 1;
        }

        var sensors = state.Sensors;
        var selection = state.Selection;
        var config = state.Config.Clone();
        Console.WriteLine($"LCD right now: {Preview(state)}");
        Console.WriteLine("Arrow keys + Enter to choose, Esc to cancel.");

        // Display: CPU or GPU in the LCD's temperature and usage fields.
        var sources = new List<(DisplaySource Source, string Text)> { (DisplaySource.Cpu, "CPU temperature and usage") };
        if (SensorSelector.AutoSelect(DisplayRole.GpuTemperature, sensors) is not null)
            sources.Add((DisplaySource.Gpu, "GPU temperature and usage (the LCD labels still say CPU)"));
        var current = ConfigRules.GetDisplaySource(selection, sensors);
        int choice = ConsoleMenu.Choose("Display:", sources.Select(s => s.Text).ToList(), sources.FindIndex(s => s.Source == current));
        if (choice < 0)
            return Cancelled();
        ConfigRules.SetDisplaySource(config, sources[choice].Source, sensors);

        // Fan
        var fans = sensors.Where(s => s.Kind == SensorKind.Fan)
            .Select(s => (s.Id, Text: $"{s.Hardware} / {s.Name}  ({Format(s.Value)} RPM)"))
            .Prepend((Id: SensorSelector.None, Text: "Off"))
            .ToList();
        var fanId = selection.GetValueOrDefault(DisplayRole.FanSpeed) ?? SensorSelector.None;
        choice = ConsoleMenu.Choose("Fan:", fans.Select(f => f.Text).ToList(), Math.Max(0, fans.FindIndex(f => f.Id == fanId)));
        if (choice < 0)
            return Cancelled();
        config.Sensors[DisplayRole.FanSpeed] = fans[choice].Id;

        // Unit
        var units = Enum.GetValues<TemperatureUnit>();
        choice = ConsoleMenu.Choose("Temperature unit:", ["°C", "°F"], Array.IndexOf(units, config.TemperatureUnit));
        if (choice < 0)
            return Cancelled();
        config.TemperatureUnit = units[choice];

        // Update interval
        var intervals = IntervalChoices.Append(config.SensorIntervalMs).Distinct().Order().ToList();
        choice = ConsoleMenu.Choose("Update interval:",
            intervals.Select(ms => $"{ms / 1000.0:0.#} s").ToList(), intervals.IndexOf(config.SensorIntervalMs));
        if (choice < 0)
            return Cancelled();
        config.SensorIntervalMs = intervals[choice];

        try
        {
            var response = await client.SetConfigAsync(config);
            if (!response.Ok)
            {
                Console.Error.WriteLine($"Not saved: {response.Error}");
                return 1;
            }
        }
        catch (Exception e) when (IsConnectionError(e))
        {
            Console.Error.WriteLine("The ttlcd service is not reachable. Is it installed and running?");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("Saved - the LCD uses the new settings.");
        return 0;
    }

    private static int Cancelled()
    {
        Console.WriteLine();
        Console.WriteLine("Cancelled, nothing changed.");
        return 0;
    }

    /// <summary>The three values the LA series LCD shows.</summary>
    private static string Preview(IpcResponse state)
    {
        double? Value(DisplayRole role) =>
            state.Selection!.TryGetValue(role, out var id) ? state.Sensors!.FirstOrDefault(s => s.Id == id)?.Value : null;

        var fahrenheit = state.Config!.TemperatureUnit == TemperatureUnit.Fahrenheit;
        var temperature = Value(DisplayRole.CpuTemperature) is { } c ? (fahrenheit ? c * 9 / 5 + 32 : c) : (double?)null;
        return $"{Format(temperature)} {(fahrenheit ? "°F" : "°C")}, {Format(Value(DisplayRole.CpuUsage))} %, " +
               $"{Format(Value(DisplayRole.FanSpeed))} RPM";
    }

    private static string Format(double? value) =>
        value is { } v && double.IsFinite(v) ? Math.Floor(v).ToString("0") : "-";

    private static bool IsConnectionError(Exception e) =>
        e is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException
            or InvalidDataException or JsonException;
}
