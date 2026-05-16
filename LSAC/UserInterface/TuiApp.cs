namespace LSAC.UserInterface;

using System.Text;

public sealed class TuiApp
{
    private readonly ConfigModel _config;
    private bool _inAlt;

    public TuiApp(ConfigModel config)
    {
        _config = config;
    }

    public void Run()
    {
        if (_config.Dialog == null)
        {
            Console.WriteLine("No dialog configuration found. Run 'LSAC setup' to create one.");
            return;
        }

        Console.OutputEncoding = Encoding.UTF8;
        Utils.SaveTerminalState();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Cleanup();
            Environment.Exit(0);
        };

        EnterAlt();
        try
        {
            RunMenu(
                _config.Dialog.Title,
                _config.Dialog.Options,
                Utils.ResolveCwd(null, _config.Dialog.Cwd),
                isRoot: true);
        }
        finally
        {
            Cleanup();
        }
    }

    // ── terminal helpers ────────────────────────────────────────────────────

    private void Cleanup()
    {
        if (_inAlt) ExitAlt();
        Write("\x1b[?25h"); // ensure cursor visible
    }

    private void EnterAlt()
    {
        Write("\x1b[?1049h\x1b[?25l\x1b[2J\x1b[H");
        _inAlt = true;
    }

    private void ExitAlt()
    {
        Write("\x1b[?25h\x1b[?1049l");
        _inAlt = false;
    }

    private static void Write(string s)
    {
        Console.Write(s);
        Console.Out.Flush();
    }

    // ── menu loop ────────────────────────────────────────────────────────────

    private void RunMenu(string title, List<DialogOption> options, string? cwd, bool isRoot)
    {
        int sel = 0;
        int scroll = 0;

        while (true)
        {
            int vis = VisibleCount(options.Count);
            scroll = AdjustScroll(sel, scroll, vis, options.Count);
            Render(title, options, sel, scroll, isRoot);

            ConsoleKeyInfo key;
            try { key = Console.ReadKey(intercept: true); }
            catch { return; }

            bool noMod = (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0;

            if ((key.Key == ConsoleKey.UpArrow || (key.Key == ConsoleKey.K && noMod)) && sel > 0)
            {
                sel--;
            }
            else if ((key.Key == ConsoleKey.DownArrow || (key.Key == ConsoleKey.J && noMod)) && sel < options.Count - 1)
            {
                sel++;
            }
            else if (key.Key == ConsoleKey.Enter)
            {
                if (options.Count > 0)
                    Activate(options[sel], cwd);
            }
            else if (key.Key == ConsoleKey.Escape || key.Key == ConsoleKey.Backspace)
            {
                return;
            }
            else if (key.Key == ConsoleKey.Q && noMod && isRoot)
            {
                return;
            }
        }
    }

    private void Activate(DialogOption opt, string? cwd)
    {
        if (opt.IsSubmenu)
        {
            RunMenu(
                opt.Title ?? opt.Name,
                opt.Options ?? [],
                Utils.ResolveCwd(cwd, opt.Cwd),
                isRoot: false);
        }
        else
        {
            RunCommand(opt, cwd);
        }
    }

    // ── command execution ────────────────────────────────────────────────────

    private void RunCommand(DialogOption opt, string? cwd)
    {
        if (string.IsNullOrWhiteSpace(opt.Command))
        {
            ShowError("No command configured for this option.");
            return;
        }

        ExitAlt();
        Utils.ApplyConsoleForInteractive();

        string sep = new('─', Math.Min(Math.Max(20, Console.WindowWidth - 1), 80));
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine(sep);
        Console.ResetColor();
        Console.WriteLine($" {opt.Name}");
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine(sep);
        Console.ResetColor();

        Utils.ExecuteShellCommandInteractive(opt.Command, Utils.ResolveCwd(cwd, opt.Cwd));

        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine(sep);
        Console.ResetColor();
        Console.Write("Press any key to return...");

        Utils.DrainConsoleInput();
        Console.ReadKey(true);
        Console.WriteLine();

        EnterAlt();
    }

    private void ShowError(string msg)
    {
        int w = Math.Max(1, Console.WindowWidth);
        int h = Math.Max(3, Console.WindowHeight);
        int row = h / 2;
        string line1 = $"  Error: {msg}  ";
        string line2 = "  Press any key...  ";
        int col = Math.Max(1, (w - line1.Length) / 2 + 1);
        Write($"\x1b[{row};{col}H\x1b[41;97m{line1}\x1b[0m\x1b[{row + 1};{col}H\x1b[2m{line2}\x1b[0m");
        Console.ReadKey(true);
    }

    // ── rendering ────────────────────────────────────────────────────────────

    private void Render(string title, List<DialogOption> options, int sel, int scroll, bool isRoot)
    {
        int w = Math.Max(20, Console.WindowWidth);
        int h = Math.Max(5, Console.WindowHeight);
        int inner = w - 2;
        int baseVis = Math.Max(1, h - 8);
        bool needsScroll = options.Count > baseVis;
        int vis = needsScroll ? Math.Max(1, h - 10) : baseVis;

        var sb = new StringBuilder(w * h * 2);
        sb.Append("\x1b[H\x1b[0m");

        // top border + title
        BoxLine(sb, '┌', '─', '┐', inner);
        string t = Trim($" {title} ", inner);
        sb.Append("│\x1b[1;36m").Append(t).Append(' ', inner - t.Length).Append("\x1b[0m│\r\n");
        BoxLine(sb, '├', '─', '┤', inner);

        // padding row
        sb.Append("│").Append(' ', inner).Append("│\r\n");

        // items
        int end = Math.Min(scroll + vis, options.Count);
        for (int i = scroll; i < end; i++)
        {
            var opt = options[i];
            bool hi = i == sel;

            string label = opt.Name;
            if (!string.IsNullOrEmpty(opt.Description)) label += $" - {opt.Description}";
            if (opt.IsSubmenu) label += " >";
            label = Trim(label, inner - 3);

            int pad = inner - 3 - label.Length;
            string cursor = hi ? "► " : "  ";

            sb.Append("│ ");
            if (hi) sb.Append("\x1b[7m");
            sb.Append(cursor).Append(label).Append(' ', Math.Max(0, pad));
            if (hi) sb.Append("\x1b[0m");
            sb.Append("│\r\n");
        }

        // fill empty item rows
        for (int i = end - scroll; i < vis; i++)
            sb.Append("│").Append(' ', inner).Append("│\r\n");

        // padding row
        sb.Append("│").Append(' ', inner).Append("│\r\n");

        // optional scroll indicator
        if (needsScroll)
        {
            string info = Trim($" {scroll + 1}–{Math.Min(scroll + vis, options.Count)}/{options.Count} ", inner);
            BoxLine(sb, '├', '─', '┤', inner);
            sb.Append("│\x1b[2m").Append(info).Append(' ', inner - info.Length).Append("\x1b[0m│\r\n");
        }

        // hint bar + bottom border
        BoxLine(sb, '├', '─', '┤', inner);
        string hint = Trim(
            isRoot ? " ↑↓/jk Move   Enter Select   q/Esc Quit"
                   : " ↑↓/jk Move   Enter Select   Esc Back",
            inner);
        sb.Append("│\x1b[2m").Append(hint).Append(' ', inner - hint.Length).Append("\x1b[0m│\r\n");

        // Bottom border: no \r\n — a trailing newline on the last terminal row
        // causes a 1-line scroll, shifting the whole box up by one.
        sb.Append('└').Append('─', Math.Max(0, inner)).Append('┘');

        // Erase anything to the right of the bottom border and below.
        sb.Append("\x1b[J");

        Write(sb.ToString());
    }

    private static void BoxLine(StringBuilder sb, char l, char m, char r, int inner)
        => sb.Append(l).Append(m, Math.Max(0, inner)).Append(r).Append("\r\n");

    private static string Trim(string s, int max)
    {
        if (max <= 0) return string.Empty;
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }

    private static int VisibleCount(int optionsCount)
    {
        int baseVis = Math.Max(1, Console.WindowHeight - 8);
        // Scroll indicator adds 2 extra lines (separator + info); reduce vis to compensate.
        return optionsCount > baseVis ? Math.Max(1, Console.WindowHeight - 10) : baseVis;
    }

    private static int AdjustScroll(int sel, int scroll, int vis, int count)
    {
        if (count == 0) return 0;
        if (sel < scroll) return sel;
        if (sel >= scroll + vis) return sel - vis + 1;
        return scroll;
    }
}