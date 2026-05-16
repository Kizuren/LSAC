namespace LSAC.UserInterface;

using System.Text;

public sealed class SetupApp
{
    private readonly DialogConfig _dialog;
    private bool _inAlt;

    public SetupApp()
    {
        Config.Model.Dialog ??= new DialogConfig();
        _dialog = Config.Model.Dialog;
    }

    public void Run()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Utils.SaveTerminalState();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; Cleanup(); Environment.Exit(0); };
        EnterAlt();
        try { PageMain(); }
        finally { Cleanup(); }
    }

    // ── terminal ─────────────────────────────────────────────────────────────

    private void Cleanup() { if (_inAlt) ExitAlt(); Out("\x1b[?25h"); }
    private void EnterAlt() { Out("\x1b[?1049h\x1b[?25l\x1b[2J\x1b[H"); _inAlt = true; }
    private void ExitAlt() { Out("\x1b[?25h\x1b[?1049l"); _inAlt = false; }
    private static void Out(string s) { Console.Write(s); Console.Out.Flush(); }

    // ── page: dialog settings ────────────────────────────────────────────────

    private void PageMain()
    {
        // 0=Title  1=CWD  2=Options  3=Save  4=Cancel
        int sel = 0;
        string? editLabel = null;
        StringBuilder? editBuf = null;

        while (true)
        {
            DrawMain(sel, editLabel, editBuf?.ToString());
            var key = Console.ReadKey(intercept: true);
            bool ctrl = (key.Modifiers & ConsoleModifiers.Control) != 0;
            bool noMod = !ctrl && (key.Modifiers & ConsoleModifiers.Alt) == 0;

            if (editBuf != null)
            {
                switch (key.Key)
                {
                    case ConsoleKey.Enter:
                        if (sel == 0) _dialog.Title = string.IsNullOrWhiteSpace(editBuf.ToString()) ? "Main Menu" : editBuf.ToString();
                        else _dialog.Cwd = string.IsNullOrWhiteSpace(editBuf.ToString()) ? null : editBuf.ToString();
                        editBuf = null; editLabel = null;
                        break;
                    case ConsoleKey.Escape:
                        editBuf = null; editLabel = null;
                        break;
                    case ConsoleKey.Backspace:
                        if (editBuf.Length > 0) editBuf.Remove(editBuf.Length - 1, 1);
                        break;
                    default:
                        if (!char.IsControl(key.KeyChar) && key.KeyChar != '\0') editBuf.Append(key.KeyChar);
                        break;
                }
                continue;
            }

            if ((key.Key == ConsoleKey.UpArrow || (key.Key == ConsoleKey.K && noMod)) && sel > 0) sel--;
            else if ((key.Key == ConsoleKey.DownArrow || (key.Key == ConsoleKey.J && noMod)) && sel < 4) sel++;
            else if (key.Key == ConsoleKey.Enter)
            {
                if (sel == 0) { editLabel = "Title"; editBuf = new(_dialog.Title); }
                else if (sel == 1) { editLabel = "CWD"; editBuf = new(_dialog.Cwd ?? ""); }
                else if (sel == 2) PageOptions(_dialog.Options, _dialog.Cwd, "Options");
                else if (sel == 3) { TrySave(); return; }
                else return;
            }
            else if (key.Key == ConsoleKey.S && ctrl) { TrySave(); return; }
            else if (key.Key == ConsoleKey.Escape) return;
        }
    }

    private void DrawMain(int sel, string? editLabel, string? editValue)
    {
        int w = Math.Max(20, Console.WindowWidth), inner = w - 2;
        var sb = new StringBuilder();
        sb.Append("\x1b[H\x1b[0m");
        HBar(sb, '┌', inner, '┐');
        TitleRow(sb, inner, "LSAC Setup — Dialog Settings");
        HBar(sb, '├', inner, '┤');
        Blank(sb, inner);
        FieldRow(sb, inner, sel == 0, "Title:  ", _dialog.Title);
        FieldRow(sb, inner, sel == 1, "CWD:    ", _dialog.Cwd ?? "(none — inherits from shell)");
        Blank(sb, inner);
        FieldRow(sb, inner, sel == 2, "→ ", $"Manage Options  ({_dialog.Options.Count} items)");
        Blank(sb, inner);
        FieldRow(sb, inner, sel == 3, "  ", "Save & Exit");
        FieldRow(sb, inner, sel == 4, "  ", "Exit without saving");
        Blank(sb, inner);
        HBar(sb, '├', inner, '┤');
        HintOrInput(sb, inner, editLabel, editValue, " ↑↓/jk Move   Enter Edit/Open   Ctrl+S Save   Esc Quit");
        sb.Append('└').Append('─', Math.Max(0, inner)).Append('┘').Append("\x1b[J");
        Out(sb.ToString());
    }

    // ── page: options list ───────────────────────────────────────────────────

    private void PageOptions(List<DialogOption> options, string? parentCwd, string title)
    {
        int sel = 0, scroll = 0;
        while (true)
        {
            int vis = Math.Max(1, Console.WindowHeight - 8);
            scroll = Clamp(sel, scroll, vis, options.Count);
            DrawOptions(title, options, sel, scroll, vis);
            var key = Console.ReadKey(intercept: true);
            bool noMod = (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0;

            if ((key.Key == ConsoleKey.UpArrow || (key.Key == ConsoleKey.K && noMod)) && sel > 0) sel--;
            else if ((key.Key == ConsoleKey.DownArrow || (key.Key == ConsoleKey.J && noMod)) && sel < options.Count - 1) sel++;
            else if ((key.Key == ConsoleKey.Enter || (key.Key == ConsoleKey.E && noMod)) && options.Count > 0)
                PageEditOption(options[sel], parentCwd);
            else if (key.Key == ConsoleKey.A && noMod)
            {
                var newOpt = new DialogOption { Name = "new-option" };
                if (PageEditOption(newOpt, parentCwd)) { options.Add(newOpt); sel = options.Count - 1; }
            }
            else if ((key.Key == ConsoleKey.D || key.Key == ConsoleKey.Delete) && options.Count > 0 && noMod)
            {
                if (Confirm($"Delete '{options[sel].Name}'?"))
                {
                    options.RemoveAt(sel);
                    if (sel >= options.Count) sel = Math.Max(0, options.Count - 1);
                }
            }
            else if (key.Key == ConsoleKey.Escape) return;
        }
    }

    private void DrawOptions(string title, List<DialogOption> options, int sel, int scroll, int vis)
    {
        int w = Math.Max(20, Console.WindowWidth), inner = w - 2;
        var sb = new StringBuilder();
        sb.Append("\x1b[H\x1b[0m");
        HBar(sb, '┌', inner, '┐');
        TitleRow(sb, inner, $"LSAC Setup — {title}");
        HBar(sb, '├', inner, '┤');
        Blank(sb, inner);

        if (options.Count == 0)
        {
            string empty = Trim("  (no options — press A to add one)", inner);
            sb.Append("│\x1b[2m").Append(empty).Append(' ', inner - empty.Length).Append("\x1b[0m│\r\n");
            for (int i = 1; i < vis; i++) Blank(sb, inner);
        }
        else
        {
            int end = Math.Min(scroll + vis, options.Count);
            for (int i = scroll; i < end; i++)
            {
                var opt = options[i];
                bool hi = i == sel;
                string detail = opt.Options != null
                    ? $"  ({opt.Options.Count} sub-items)"
                    : (!string.IsNullOrEmpty(opt.Command) ? $"  [{Trim(opt.Command, 20)}]" : "");
                string label = Trim(opt.Name + detail, inner - 3);
                int pad = inner - 3 - label.Length;
                sb.Append("│ ");
                if (hi) sb.Append("\x1b[7m");
                sb.Append(hi ? "► " : "  ").Append(label).Append(' ', Math.Max(0, pad));
                if (hi) sb.Append("\x1b[0m");
                sb.Append("│\r\n");
            }
            for (int i = end - scroll; i < vis; i++) Blank(sb, inner);
        }

        Blank(sb, inner);
        HBar(sb, '├', inner, '┤');
        HintOrInput(sb, inner, null, null, " A Add   Enter/E Edit   D Delete   Esc Back");
        sb.Append('└').Append('─', Math.Max(0, inner)).Append('┘').Append("\x1b[J");
        Out(sb.ToString());
    }

    // ── page: edit option ────────────────────────────────────────────────────

    private bool PageEditOption(DialogOption opt, string? parentCwd)
    {
        var c = new DialogOption
        {
            Name = opt.Name,
            Description = opt.Description,
            Title = opt.Title,
            Cwd = opt.Cwd,
            Command = opt.Command,
            Options = opt.Options != null ? new List<DialogOption>(opt.Options) : null
        };

        // Fields:  0=Name  1=Description  2=Title  3=CWD  4=Command  5=Submenu toggle
        //          6=Sub-entries (only when isSub)
        //          saveIdx = isSub ? 7 : 6
        //          cancelIdx = isSub ? 8 : 7
        int sel = 0;
        string? editLabel = null;
        StringBuilder? editBuf = null;

        while (true)
        {
            bool isSub = c.Options != null;
            int saveIdx = isSub ? 7 : 6;
            int cancelIdx = isSub ? 8 : 7;

            DrawEditOption(c, sel, editLabel, editBuf?.ToString());
            var key = Console.ReadKey(intercept: true);
            bool noMod = (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0;

            if (editBuf != null)
            {
                switch (key.Key)
                {
                    case ConsoleKey.Enter:
                        ApplyField(c, sel, editBuf.ToString());
                        editBuf = null; editLabel = null;
                        break;
                    case ConsoleKey.Escape:
                        editBuf = null; editLabel = null;
                        break;
                    case ConsoleKey.Backspace:
                        if (editBuf.Length > 0) editBuf.Remove(editBuf.Length - 1, 1);
                        break;
                    default:
                        if (!char.IsControl(key.KeyChar) && key.KeyChar != '\0') editBuf.Append(key.KeyChar);
                        break;
                }
                continue;
            }

            if ((key.Key == ConsoleKey.UpArrow || (key.Key == ConsoleKey.K && noMod)) && sel > 0) sel--;
            else if ((key.Key == ConsoleKey.DownArrow || (key.Key == ConsoleKey.J && noMod)) && sel < cancelIdx) sel++;
            else if (key.Key == ConsoleKey.Enter)
            {
                if (sel == 5)
                {
                    c.Options = c.Options == null ? [] : null;
                    if (sel > (c.Options != null ? 8 : 7)) sel = c.Options != null ? 8 : 7;
                }
                else if (isSub && sel == 6) PageOptions(c.Options!, c.Cwd ?? parentCwd, c.Title ?? c.Name);
                else if (sel == saveIdx)
                {
                    if (string.IsNullOrWhiteSpace(c.Name)) { ShowMessage("Name is required."); continue; }
                    if (!isSub && string.IsNullOrWhiteSpace(c.Command)) { ShowMessage("Command is required for non-submenu options."); continue; }
                    opt.Name = c.Name; opt.Description = c.Description; opt.Title = c.Title;
                    opt.Cwd = c.Cwd; opt.Command = c.Command; opt.Options = c.Options;
                    return true;
                }
                else if (sel == cancelIdx) return false;
                else (editLabel, editBuf) = FieldEditor(c, sel);
            }
            else if (key.Key == ConsoleKey.Escape) return false;
        }
    }

    private void DrawEditOption(DialogOption opt, int sel, string? editLabel, string? editValue)
    {
        bool isSub = opt.Options != null;
        int saveIdx = isSub ? 7 : 6;
        int cancelIdx = isSub ? 8 : 7;
        int w = Math.Max(20, Console.WindowWidth), inner = w - 2;
        var sb = new StringBuilder();
        sb.Append("\x1b[H\x1b[0m");
        HBar(sb, '┌', inner, '┐');
        TitleRow(sb, inner, "LSAC Setup — Edit Option");
        HBar(sb, '├', inner, '┤');
        Blank(sb, inner);
        FieldRow(sb, inner, sel == 0, "Name:        ", opt.Name);
        FieldRow(sb, inner, sel == 1, "Description: ", opt.Description ?? "");
        FieldRow(sb, inner, sel == 2, "Title:       ", opt.Title ?? "");
        FieldRow(sb, inner, sel == 3, "CWD:         ", opt.Cwd ?? "");
        FieldRow(sb, inner, sel == 4, "Command:     ", opt.Command ?? "");
        FieldRow(sb, inner, sel == 5, "Submenu:     ", isSub ? "Yes  (Enter to toggle)" : "No   (Enter to toggle)");
        if (isSub) FieldRow(sb, inner, sel == 6, "→ ", $"Manage sub-entries  ({opt.Options!.Count} items)");
        Blank(sb, inner);
        FieldRow(sb, inner, sel == saveIdx,   "  ", "Save");
        FieldRow(sb, inner, sel == cancelIdx, "  ", "Cancel");
        Blank(sb, inner);
        HBar(sb, '├', inner, '┤');
        HintOrInput(sb, inner, editLabel, editValue, " ↑↓ Move   Enter Edit/Toggle/Open   Esc Cancel");
        sb.Append('└').Append('─', Math.Max(0, inner)).Append('┘').Append("\x1b[J");
        Out(sb.ToString());
    }

    private static (string label, StringBuilder buf) FieldEditor(DialogOption opt, int idx) => idx switch
    {
        0 => ("Name",        new StringBuilder(opt.Name)),
        1 => ("Description", new StringBuilder(opt.Description ?? "")),
        2 => ("Title",       new StringBuilder(opt.Title ?? "")),
        3 => ("CWD",         new StringBuilder(opt.Cwd ?? "")),
        4 => ("Command",     new StringBuilder(opt.Command ?? "")),
        _ => throw new InvalidOperationException()
    };

    private static void ApplyField(DialogOption opt, int idx, string value)
    {
        switch (idx)
        {
            case 0: opt.Name        = value.Trim(); break;
            case 1: opt.Description = string.IsNullOrWhiteSpace(value) ? null : value; break;
            case 2: opt.Title       = string.IsNullOrWhiteSpace(value) ? null : value; break;
            case 3: opt.Cwd        = string.IsNullOrWhiteSpace(value) ? null : value; break;
            case 4: opt.Command    = string.IsNullOrWhiteSpace(value) ? null : value; break;
        }
    }

    // ── save / dialogs ────────────────────────────────────────────────────────

    private void TrySave()
    {
        Config.Model = new ConfigModel { Dialog = _dialog };
        // Capture any Console.WriteLine output from BackupConfig/SaveConfig
        var orig = Console.Out;
        Console.SetOut(new StringWriter());
        Config.BackupConfig();
        bool saved = Config.SaveConfig();
        Console.SetOut(orig);
        ShowMessage(saved ? "Configuration saved successfully." : "Failed to save configuration.");
    }

    private bool Confirm(string question)
    {
        int w = Math.Max(20, Console.WindowWidth), inner = w - 2;
        var sb = new StringBuilder();
        sb.Append("\x1b[H\x1b[0m");
        HBar(sb, '┌', inner, '┐');
        TitleRow(sb, inner, "Confirm");
        HBar(sb, '├', inner, '┤');
        Blank(sb, inner);
        string msg = Trim("  " + question, inner);
        sb.Append("│").Append(msg).Append(' ', inner - msg.Length).Append("│\r\n");
        Blank(sb, inner);
        HBar(sb, '├', inner, '┤');
        string hint = Trim(" Y Yes   N/Esc No", inner);
        sb.Append("│\x1b[2m").Append(hint).Append(' ', inner - hint.Length).Append("\x1b[0m│\r\n");
        sb.Append('└').Append('─', Math.Max(0, inner)).Append('┘').Append("\x1b[J");
        Out(sb.ToString());
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Y || key.Key == ConsoleKey.Enter) return true;
            if (key.Key == ConsoleKey.N || key.Key == ConsoleKey.Escape) return false;
        }
    }

    private void ShowMessage(string msg)
    {
        int w = Math.Max(20, Console.WindowWidth), inner = w - 2;
        var sb = new StringBuilder();
        sb.Append("\x1b[H\x1b[0m");
        HBar(sb, '┌', inner, '┐');
        TitleRow(sb, inner, "Notice");
        HBar(sb, '├', inner, '┤');
        Blank(sb, inner);
        string line = Trim("  " + msg, inner);
        sb.Append("│").Append(line).Append(' ', inner - line.Length).Append("│\r\n");
        Blank(sb, inner);
        HBar(sb, '├', inner, '┤');
        string hint = Trim(" Press any key to continue...", inner);
        sb.Append("│\x1b[2m").Append(hint).Append(' ', inner - hint.Length).Append("\x1b[0m│\r\n");
        sb.Append('└').Append('─', Math.Max(0, inner)).Append('┘').Append("\x1b[J");
        Out(sb.ToString());
        Console.ReadKey(true);
    }

    // ── box-drawing helpers ───────────────────────────────────────────────────

    private static void HBar(StringBuilder sb, char l, int inner, char r)
        => sb.Append(l).Append('─', Math.Max(0, inner)).Append(r).Append("\r\n");

    private static void Blank(StringBuilder sb, int inner)
        => sb.Append("│").Append(' ', inner).Append("│\r\n");

    private static void TitleRow(StringBuilder sb, int inner, string text)
    {
        string t = Trim($" {text} ", inner);
        sb.Append("│\x1b[1;36m").Append(t).Append(' ', inner - t.Length).Append("\x1b[0m│\r\n");
    }

    private static void FieldRow(StringBuilder sb, int inner, bool selected, string label, string value)
    {
        string content = Trim(label + value, inner - 1);
        int pad = inner - 1 - content.Length;
        sb.Append("│ ");
        if (selected) sb.Append("\x1b[7m");
        sb.Append(content).Append(' ', Math.Max(0, pad));
        if (selected) sb.Append("\x1b[0m");
        sb.Append("│\r\n");
    }

    private static void HintOrInput(StringBuilder sb, int inner, string? editLabel, string? editValue, string defaultHint)
    {
        if (editLabel != null)
        {
            string prompt = Trim($" {editLabel}: {editValue}_", inner);
            sb.Append("│\x1b[?25h").Append(prompt).Append(' ', inner - prompt.Length).Append("│\r\n");
        }
        else
        {
            string hint = Trim(defaultHint, inner);
            sb.Append("│\x1b[2m").Append(hint).Append(' ', inner - hint.Length).Append("\x1b[0m│\r\n");
        }
    }

    private static string Trim(string s, int max)
    {
        if (max <= 0) return string.Empty;
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }

    private static int Clamp(int sel, int scroll, int vis, int count)
    {
        if (count == 0) return 0;
        if (sel < scroll) return sel;
        if (sel >= scroll + vis) return sel - vis + 1;
        return scroll;
    }

    public static void ShowHelp()
    {
        Console.WriteLine("""
            LSAC Setup - Configuration Wizard

            Usage:
              LSAC setup [OPTIONS]

            Options:
              -f, --file <path>     Use a custom configuration file path
              -h, --help            Show this help message

            Examples:
              LSAC setup                          # Use default config location
              LSAC setup -f ./custom.config       # Use custom config file
            """);
    }
}