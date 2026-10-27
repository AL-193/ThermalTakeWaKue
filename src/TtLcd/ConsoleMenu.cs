namespace TtLcd;

internal static class ConsoleMenu
{
    /// <summary>
    /// Lets the user pick an option with the arrow keys and Enter (or by number when
    /// input is redirected). Returns the chosen index, or -1 when cancelled with Esc.
    /// </summary>
    public static int Choose(string title, IReadOnlyList<string> options, int selected)
    {
        Console.WriteLine();
        Console.WriteLine(title);
        selected = Math.Clamp(selected, 0, options.Count - 1);
        return Console.IsInputRedirected || Console.IsOutputRedirected
            ? ChooseByNumber(options, selected)
            : ChooseWithKeys(options, selected);
    }

    private static int ChooseWithKeys(IReadOnlyList<string> options, int selected)
    {
        // Reserve the lines first so a scrolling console does not shift the menu.
        for (int i = 0; i < options.Count; i++)
            Console.WriteLine();
        int top = Console.CursorTop - options.Count;
        int width = Math.Max(20, Console.WindowWidth - 1);

        Console.CursorVisible = false;
        try
        {
            while (true)
            {
                for (int i = 0; i < options.Count; i++)
                {
                    Console.SetCursorPosition(0, top + i);
                    if (i == selected)
                        Console.ForegroundColor = ConsoleColor.Cyan;
                    var line = (i == selected ? " > " : "   ") + options[i];
                    Console.Write(line.Length > width ? line[..width] : line.PadRight(width));
                    Console.ResetColor();
                }

                switch (Console.ReadKey(intercept: true).Key)
                {
                    case ConsoleKey.UpArrow:
                        selected = (selected - 1 + options.Count) % options.Count;
                        break;
                    case ConsoleKey.DownArrow:
                        selected = (selected + 1) % options.Count;
                        break;
                    case ConsoleKey.Enter:
                        return selected;
                    case ConsoleKey.Escape:
                        return -1;
                }
            }
        }
        finally
        {
            Console.SetCursorPosition(0, top + options.Count);
            Console.CursorVisible = true;
        }
    }

    private static int ChooseByNumber(IReadOnlyList<string> options, int selected)
    {
        for (int i = 0; i < options.Count; i++)
            Console.WriteLine($" {(i == selected ? '*' : ' ')} {i + 1}) {options[i]}");
        Console.Write($"Number [{selected + 1}]: ");
        var input = Console.ReadLine();
        if (input is null)
            return -1;
        if (string.IsNullOrWhiteSpace(input))
            return selected;
        return int.TryParse(input, out var n) && n >= 1 && n <= options.Count ? n - 1 : -1;
    }
}
