using System.Globalization;

namespace TtLcd.Settings;

/// <summary>UI texts in German or English, following the Windows/desktop language.</summary>
internal static class Strings
{
    private static readonly bool German = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de";

    private static string T(string english, string german) => German ? german : english;

    public static string Title => T("ttlcd – LCD settings", "ttlcd – LCD-Einstellungen");
    public static string PreviewLabel => T("LCD right now", "LCD gerade");
    public static string Display => T("Display", "Anzeige");
    public static string Fan => T("Fan", "Lüfter");
    public static string Unit => T("Unit", "Einheit");
    public static string Interval => T("Update interval", "Aktualisierung");
    public static string Apply => T("Apply", "Übernehmen");
    public static string Reload => T("Reload", "Neu laden");
    public static string Off => T("Off", "Aus");
    public static string Connecting => T("Connecting to the ttlcd service …", "Verbinde mit dem ttlcd-Dienst …");
    public static string Saving => T("Saving …", "Speichere …");
    public static string Connected => T("Connected to the ttlcd service.", "Verbunden mit dem ttlcd-Dienst.");
    public static string Saved => T("Saved – the LCD uses the new settings.", "Gespeichert – das LCD nutzt die neuen Einstellungen.");
    public static string NotReachable => T(
        "The ttlcd service is not reachable. Is it installed and running?",
        "Der ttlcd-Dienst ist nicht erreichbar. Ist er installiert und gestartet?");
    public static string GpuNote => T(
        "The LCD's labels stay \"CPU\" – in GPU mode they show the graphics card's temperature and usage.",
        "Die Beschriftung auf dem LCD bleibt „CPU“ – im GPU-Modus stehen dort Temperatur und Auslastung der Grafikkarte.");

    public static string Error(string? message) => T($"Error: {message}", $"Fehler: {message}");

    public static string Seconds(int milliseconds) =>
        (milliseconds / 1000.0).ToString("0.#", CultureInfo.CurrentCulture) + " s";
}
