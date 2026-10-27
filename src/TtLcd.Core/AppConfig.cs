using System.Text.Json;
using System.Text.Json.Serialization;
using TtLcd.Protocol;
using TtLcd.Sensors;

namespace TtLcd;

public sealed class AppConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public TemperatureUnit TemperatureUnit { get; set; } = TemperatureUnit.Celsius;

    /// <summary>How often sensors are read.</summary>
    public int SensorIntervalMs { get; set; } = 1000;

    /// <summary>How often a packet is sent; the original software uses 250 ms.</summary>
    public int SendIntervalMs { get; set; } = 250;

    /// <summary>Sensor ID per LCD field (see <c>ttlcd list</c>). Missing entries are chosen automatically.</summary>
    public Dictionary<DisplayRole, string> Sensors { get; set; } = [];

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ttlcd", "config.json");

    /// <summary>Loads the file, or returns defaults when it does not exist.</summary>
    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
            return new AppConfig();
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<AppConfig>(stream, JsonOptions) ?? new AppConfig();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    public AppConfig Clone() =>
        JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;
}
