namespace TtLcd;

internal static class Log
{
    private const long MaxFileSize = 1_000_000;

    private static readonly Lock Gate = new();
    private static StreamWriter? _file;

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    /// <summary>Also writes to a file; used when running as a Windows service, which has no console.</summary>
    public static void ToFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path) && new FileInfo(path).Length > MaxFileSize)
            File.Move(path, path + ".old", overwrite: true);
        _file = new StreamWriter(path, append: true) { AutoFlush = true };
    }

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}";
        lock (Gate)
        {
            Console.Error.WriteLine(line);
            _file?.WriteLine(line);
        }
    }
}
