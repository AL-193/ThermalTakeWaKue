using System.Runtime.InteropServices;
using Avalonia;

namespace TtLcd.Settings;

internal static partial class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        HideHostConsole();
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Started as <c>dotnet ttlcd-settings.dll</c> (needed while builds are unsigned, see README),
    /// Windows opens a console window for dotnet.exe. Close it when it belongs to this process only.
    /// </summary>
    private static void HideHostConsole()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var processes = new uint[2];
        if (GetConsoleProcessList(processes, (uint)processes.Length) == 1)
            FreeConsole();
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint GetConsoleProcessList([Out] uint[] processList, uint processCount);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FreeConsole();
}
