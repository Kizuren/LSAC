namespace LSAC.UserInterface;

using System.Collections.ObjectModel;
using System.Reflection;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

public class UiBuilder
{
    private readonly IApplication _app;
    private readonly ConfigModel _config;

    public UiBuilder(IApplication app, ConfigModel config)
    {
        _app = app;
        _config = config;
    }

    public void Run()
    {
        if (_config.Dialog == null)
        {
            MessageBox.Query(_app, "Missing Config", "No dialog configuration found.", "Ok");
            return;
        }

        var rootCwd = Utils.ResolveCwd(null, _config.Dialog.Cwd);
        RunMenu(_config.Dialog.Title, _config.Dialog.Options, rootCwd, isRoot: true);
    }

    private void RunMenu(string title, List<DialogOption> options, string? currentCwd, bool isRoot)
    {
        while (true)
        {
            MenuEntry? selectedEntry = null;
            var dialog = new Dialog();
            dialog.Title = title;
            dialog.Width = 70;
            dialog.Height = 20;

            var entries = BuildEntries(options);
            var listView = new ListView
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(2)
            };

            var listSource = new ObservableCollection<string>(entries.Select(entry => entry.DisplayText).ToList());
            listView.SetSource(listSource);
            if (entries.Count > 0)
            {
                listView.SelectedItem = 0;
            }

            var selectButton = new Button { X = 1, Y = Pos.Bottom(listView) + 1, Text = "Select" };
            var backButton = new Button { X = Pos.Right(selectButton) + 2, Y = Pos.Bottom(listView) + 1, Text = "Back" };

            void ActivateSelected()
            {
                var idx = listView.SelectedItem ?? -1;
                if (idx < 0 || idx >= entries.Count)
                {
                    return;
                }

                selectedEntry = entries[idx];
                _app.RequestStop();
            }

            listView.KeyDown += (_, key) =>
            {
                if (key.KeyCode == Key.Enter.KeyCode || key.KeyCode == Key.Space.KeyCode)
                {
                    key.Handled = true;
                    ActivateSelected();
                }
            };

            WireButton(selectButton, ActivateSelected);
            if (isRoot)
            {
                backButton.Text = "Exit";
            }
            WireButton(backButton, () => _app.RequestStop());

            dialog.Add(listView);
            dialog.AddButton(selectButton);
            dialog.AddButton(backButton);

            _app.Run(dialog);
            dialog.Dispose();

            if (selectedEntry == null)
            {
                return;
            }

            if (selectedEntry.IsExit)
            {
                return;
            }

            if (selectedEntry.Option == null)
            {
                continue;
            }

            if (selectedEntry.Option.IsSubmenu)
            {
                var nextTitle = selectedEntry.Option.Title ?? selectedEntry.Option.Name;
                var nextCwd = Utils.ResolveCwd(currentCwd, selectedEntry.Option.Cwd);
                RunMenu(nextTitle, selectedEntry.Option.Options ?? new List<DialogOption>(), nextCwd, isRoot: false);
                continue;
            }

            RunCommand(selectedEntry.Option, currentCwd);
            Console.Clear();
        }
    }

    private void RunCommand(DialogOption option, string? inheritedCwd)
    {
        if (string.IsNullOrWhiteSpace(option.Command))
        {
            MessageBox.Query(_app, "Missing Command", "No command configured for this option.", "Ok");
            return;
        }

        var effectiveCwd = Utils.ResolveCwd(inheritedCwd, option.Cwd);
        SuspendUi();
        Utils.ExitAlternateScreen();
        Utils.ApplyConsoleForInteractive();
        Console.Clear();
        Utils.ExecuteShellCommandInteractive(option.Command, effectiveCwd);
        Utils.DrainConsoleInput();
        Utils.DisableMouseReporting();
        Console.WriteLine("\nPress any key to continue...");
        Console.ReadKey(true);
        Utils.DrainConsoleInput();
        ResumeUi();
        Utils.ApplyConsoleForTui();
        Utils.EnableMouseReporting();
        Utils.EnterAlternateScreen();
        Console.Clear();
    }

    private void SuspendUi()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        InvokeIfExistsAny(_app, "Suspend");
        InvokeOnPropertyAny(_app, "Driver", "Suspend");
    }

    private void ResumeUi()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        InvokeOnPropertyAny(_app, "Driver", "Resume");
        InvokeIfExistsAny(_app, "Resume");
        if (_app.TopRunnableView != null)
        {
            _app.TopRunnableView.SetNeedsLayout();
            _app.TopRunnableView.SetNeedsDraw();
        }
    }

    private static void InvokeIfExistsAny(object target, params string[] methodNames)
    {
        foreach (var methodName in methodNames)
        {
            var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null || method.GetParameters().Length != 0)
            {
                continue;
            }

            method.Invoke(target, null);
            return;
        }
    }

    private static void InvokeOnPropertyAny(object target, string propertyName, params string[] methodNames)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var value = property?.GetValue(target);
        if (value == null)
        {
            return;
        }

        InvokeIfExistsAny(value, methodNames);
    }

    private static void InvokeIfExists(object target, string methodName)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        method?.Invoke(target, null);
    }

    private static void InvokeOnProperty(object target, string propertyName, string methodName)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var value = property?.GetValue(target);
        if (value != null)
        {
            InvokeIfExists(value, methodName);
        }
    }

    private static List<MenuEntry> BuildEntries(List<DialogOption> options)
    {
        var entries = new List<MenuEntry>();
        foreach (var option in options)
        {
            entries.Add(new MenuEntry(option, BuildDisplay(option)));
        }

        return entries;
    }

    private static string BuildDisplay(DialogOption option)
    {
        var label = option.Name;
        if (!string.IsNullOrWhiteSpace(option.Description))
        {
            label = $"{label} - {option.Description}";
        }

        if (option.IsSubmenu)
        {
            label = $"{label} >";
        }

        return label;
    }

    private static void WireButton(Button button, Action action)
    {
        button.Accepted += (_, _) => action();
        button.Activated += (_, _) => action();
        button.KeyDown += (_, key) =>
        {
            if (key.KeyCode == Key.Enter.KeyCode || key.KeyCode == Key.Space.KeyCode)
            {
                key.Handled = true;
                action();
            }
        };
        button.MouseEvent += (_, mouse) =>
        {
            if (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked))
            {
                mouse.Handled = true;
                action();
            }
        };
    }

    private sealed class MenuEntry
    {
        public MenuEntry(DialogOption option, string displayText)
        {
            Option = option;
            DisplayText = displayText;
        }

        private MenuEntry(string displayText, bool isExit)
        {
            DisplayText = displayText;
            IsExit = isExit;
        }

        public DialogOption? Option { get; }
        public string DisplayText { get; }
        public bool IsExit { get; }
    }
}