namespace LSAC.UserInterface;

using System.Collections.ObjectModel;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

public class UiSetup
{
    private readonly IApplication _app;
    private readonly ConfigModel _config;
    private readonly DialogConfig _dialog;

    private TextField? _dialogTitleField;
    private TextField? _dialogCwdField;
    private TextView? _reviewView;

    public UiSetup(IApplication app)
    {
        _app = app;
        _config = Config.Model;
        _dialog = _config.Dialog ?? new DialogConfig();
        _config.Dialog = _dialog;
    }

    public void RunSetupWizard()
    {
        using Wizard wiz = new();
        wiz.Title = "LSAC Setup Wizard";
        wiz.X = 0;
        wiz.Y = 0;
        wiz.Width = Dim.Fill();
        wiz.Height = Dim.Fill();

        var dialogStep = BuildDialogStep();
        var optionsStep = BuildOptionsStep();
        var reviewStep = BuildReviewStep();

        wiz.AddStep(dialogStep);
        wiz.AddStep(optionsStep);
        wiz.AddStep(reviewStep);

        wiz.StepChanging += (_, e) =>
        {
            if (e.NewValue == optionsStep || e.NewValue == reviewStep)
            {
                ApplyDialogFields();
            }
        };

        wiz.Accepting += (_, e) =>
        {
            ApplyDialogFields();
            Config.Model = _config;
            Config.BackupConfig();

            var saved = Config.SaveConfig();
            MessageBox.Query(_app, "Complete", saved ? "Configuration saved." : "Failed to save configuration.", "Ok");
            e.Handled = true;
        };

        _app.Run(wiz);
    }

    private WizardStep BuildDialogStep()
    {
        WizardStep step = new() { Title = "Dialog Settings" };
        step.HelpText = "Configure the main dialog title and default working directory.";
        step.NextButtonText = "Next";

        var titleLabel = new Label { X = 1, Y = 0, Text = "Dialog Title:", CanFocus = false };
        var cwdLabel = new Label { X = 1, Y = 3, Text = "Default CWD (leave empty for undefined):", CanFocus = false };

        _dialogTitleField = new TextField
        {
            X = 1,
            Y = 1,
            Width = 40,
            Text = _dialog.Title
        };
        _dialogTitleField.KeyDown += (_, key) =>
        {
            if (key.KeyCode == Key.Enter.KeyCode)
            {
                key.Handled = true;
                return;
            }

            if (key.KeyCode == Key.CursorDown.KeyCode || key.KeyCode == Key.CursorRight.KeyCode)
            {
                key.Handled = true;
                _dialogCwdField?.SetFocus();
            }
        };

        _dialogCwdField = new TextField
        {
            X = 1,
            Y = 4,
            Width = 60,
            Text = _dialog.Cwd ?? string.Empty
        };
        _dialogCwdField.KeyDown += (_, key) =>
        {
            if (key.KeyCode == Key.Enter.KeyCode)
            {
                key.Handled = true;
                return;
            }

            if (key.KeyCode == Key.CursorUp.KeyCode || key.KeyCode == Key.CursorLeft.KeyCode)
            {
                key.Handled = true;
                _dialogTitleField?.SetFocus();
            }
        };

        step.Add(titleLabel);
        step.Add(_dialogTitleField);
        step.Add(cwdLabel);
        step.Add(_dialogCwdField);

        step.AdvancingFocus += (_, e) =>
        {
            if (_dialogTitleField == null || _dialogCwdField == null)
                return;

            var current = step.Focused;
            if (current == _dialogTitleField && e.Direction == NavigationDirection.Forward)
            {
                e.Cancel = true;
                _dialogCwdField.SetFocus();
            }
            else if (current == _dialogCwdField && e.Direction == NavigationDirection.Backward)
            {
                e.Cancel = true;
                _dialogTitleField.SetFocus();
            }
        };

        RestrictFocusToDialogFields(step);
        _dialogTitleField.SetFocus();

        return step;
    }

    private void RestrictFocusToDialogFields(WizardStep step)
    {
        foreach (var view in step.SubViews)
        {
            if (ReferenceEquals(view, _dialogTitleField) || ReferenceEquals(view, _dialogCwdField))
                continue;

            view.CanFocus = false;
        }
    }

    private WizardStep BuildOptionsStep()
    {
        WizardStep step = new() { Title = "Options" };
        step.HelpText = "Add, edit, or delete menu options.";
        step.NextButtonText = "Next";

        var optionsList = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(2)
        };

        var optionsSource = new ObservableCollection<string>();
        optionsList.SetSource(optionsSource);
        UpdateOptionsSource(optionsSource, _dialog.Options);
        if (_dialog.Options.Count > 0)
        {
            optionsList.SelectedItem = 0;
        }

        var addButton = new Button { X = 0, Y = Pos.AnchorEnd(2), Text = "Add" };
        var editButton = new Button { X = Pos.Right(addButton) + 2, Y = Pos.AnchorEnd(2), Text = "Edit" };
        var deleteButton = new Button { X = Pos.Right(editButton) + 2, Y = Pos.AnchorEnd(2), Text = "Delete" };
        var submenuButton = new Button { X = Pos.Right(deleteButton) + 2, Y = Pos.AnchorEnd(2), Text = "Submenu" };

        void OnAdd()
        {
            var option = new DialogOption { Name = "new-option" };
            if (EditOptionDialog(option, _dialog.Cwd))
            {
                _dialog.Options.Add(option);
                UpdateOptionsSource(optionsSource, _dialog.Options);
                UpdateReview();
            }
        }

        void OnEdit()
        {
            var idx = optionsList.SelectedItem ?? -1;
            if (idx < 0 || idx >= _dialog.Options.Count)
                return;

            var option = _dialog.Options[idx];
            if (EditOptionDialog(option, _dialog.Cwd))
            {
                UpdateOptionsSource(optionsSource, _dialog.Options);
                UpdateReview();
            }
        }

        void OnDelete()
        {
            var idx = optionsList.SelectedItem ?? -1;
            if (idx < 0 || idx >= _dialog.Options.Count)
                return;

            var name = _dialog.Options[idx].Name;
            var confirm = MessageBox.Query(_app, "Delete", $"Delete option '{name}'?", "Yes", "No");
            if (confirm.GetValueOrDefault() == 0)
            {
                _dialog.Options.RemoveAt(idx);
                UpdateOptionsSource(optionsSource, _dialog.Options);
                UpdateReview();
            }
        }

        void OnSubmenu()
        {
            var idx = optionsList.SelectedItem ?? -1;
            if (idx < 0 || idx >= _dialog.Options.Count)
                return;

            var option = _dialog.Options[idx];
            option.Options ??= new List<DialogOption>();
            var parentCwd = option.Cwd ?? _dialog.Cwd;
            EditOptionsList(option.Options, option.Title ?? option.Name, parentCwd);
            UpdateOptionsSource(optionsSource, _dialog.Options);
            UpdateReview();
        }

        WireButton(addButton, OnAdd);
        WireButton(editButton, OnEdit);
        WireButton(deleteButton, OnDelete);
        WireButton(submenuButton, OnSubmenu);

        step.Add(optionsList);
        step.Add(addButton);
        step.Add(editButton);
        step.Add(deleteButton);
        step.Add(submenuButton);

        return step;
    }

    private WizardStep BuildReviewStep()
    {
        WizardStep step = new() { Title = "Review" };
        step.HelpText = "Review your configuration before saving.";
        step.NextButtonText = "Save";

        _reviewView = new TextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true
        };

        UpdateReview();
        step.Add(_reviewView);
        return step;
    }

    private void UpdateReview()
    {
        if (_reviewView == null)
            return;

        ApplyDialogFields();

        var lines = new List<string>
        {
            "Dialog:",
            $"  Title: {_dialog.Title}",
            $"  CWD: {(string.IsNullOrWhiteSpace(_dialog.Cwd) ? "(undefined)" : _dialog.Cwd)}",
            "",
            "Options:"
        };

        if (_dialog.Options.Count == 0)
        {
            lines.Add("  (none)");
        }
        else
        {
            foreach (var option in _dialog.Options)
            {
                lines.Add($"  - {FormatOptionSummary(option)}");
            }
        }

        _reviewView.Text = string.Join("\n", lines);
    }

    private static string FormatOptionSummary(DialogOption option)
    {
        var kind = option.IsSubmenu ? "submenu" : "command";
        var desc = string.IsNullOrWhiteSpace(option.Description) ? string.Empty : $" ({option.Description})";
        return $"{option.Name} [{kind}]{desc}";
    }

    private void ApplyDialogFields()
    {
        if (_dialogTitleField != null)
        {
            var title = _dialogTitleField.Text;
            _dialog.Title = string.IsNullOrWhiteSpace(title) ? "Main Menu" : title;
        }

        if (_dialogCwdField != null)
        {
            var cwd = _dialogCwdField.Text;
            _dialog.Cwd = string.IsNullOrWhiteSpace(cwd) ? null : cwd;
        }
    }

    private static void RefreshOptionsList(ListView listView, List<DialogOption> options)
    {
        var items = options
            .Select(o => o.IsSubmenu ? $"{o.Name} (submenu)" : o.Name)
            .ToList();
        listView.SetSource(new ObservableCollection<string>(items));
    }

    private static void UpdateOptionsSource(ObservableCollection<string> source, List<DialogOption> options)
    {
        source.Clear();
        foreach (var option in options)
        {
            source.Add(option.IsSubmenu ? $"{option.Name} (submenu)" : option.Name);
        }
    }

    private void EditOptionsList(List<DialogOption> options, string title, string? parentCwd)
    {
        var dialog = new Dialog();
        dialog.Title = $"Submenu: {title}";
        dialog.Width = 70;
        dialog.Height = 20;

        var list = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(3)
        };
        var listSource = new ObservableCollection<string>();
        list.SetSource(listSource);
        UpdateOptionsSource(listSource, options);

        var addButton = new Button { X = 1, Y = Pos.Bottom(list) + 1, Text = "Add" };
        var editButton = new Button { X = Pos.Right(addButton) + 2, Y = Pos.Bottom(list) + 1, Text = "Edit" };
        var deleteButton = new Button { X = Pos.Right(editButton) + 2, Y = Pos.Bottom(list) + 1, Text = "Delete" };
        var closeButton = new Button { X = Pos.Right(deleteButton) + 2, Y = Pos.Bottom(list) + 1, Text = "Close" };

        void OnAdd()
        {
            var option = new DialogOption { Name = "new-option" };
            if (EditOptionDialog(option, parentCwd))
            {
                options.Add(option);
                UpdateOptionsSource(listSource, options);
            }
        }

        void OnEdit()
        {
            var idx = list.SelectedItem ?? -1;
            if (idx < 0 || idx >= options.Count)
                return;

            if (EditOptionDialog(options[idx], parentCwd))
            {
                UpdateOptionsSource(listSource, options);
            }
        }

        void OnDelete()
        {
            var idx = list.SelectedItem ?? -1;
            if (idx < 0 || idx >= options.Count)
                return;

            var name = options[idx].Name;
            var confirm = MessageBox.Query(_app, "Delete", $"Delete option '{name}'?", "Yes", "No");
            if (confirm.GetValueOrDefault() == 0)
            {
                options.RemoveAt(idx);
                UpdateOptionsSource(listSource, options);
            }
        }

        void OnClose()
        {
            _app.RequestStop();
            _app.TopRunnableView?.SetNeedsDraw();
        }

        WireButton(addButton, OnAdd);
        WireButton(editButton, OnEdit);
        WireButton(deleteButton, OnDelete);
        WireButton(closeButton, OnClose);

        dialog.Add(list);
        dialog.AddButton(addButton);
        dialog.AddButton(editButton);
        dialog.AddButton(deleteButton);
        dialog.AddButton(closeButton);
        _app.Run(dialog);
        _app.TopRunnableView?.SetNeedsDraw();
        dialog.Dispose();
    }

    private bool EditOptionDialog(DialogOption option, string? defaultCwd)
    {
        var dialog = new Dialog();
        dialog.Title = "Edit Option";
        dialog.Width = 70;
        dialog.Height = 20;

        var hadExplicitCwd = !string.IsNullOrWhiteSpace(option.Cwd);
        var cwdSeed = hadExplicitCwd ? option.Cwd : defaultCwd;

        var nameField = new TextField { X = 18, Y = 1, Width = 40, Text = option.Name };
        var descField = new TextField { X = 18, Y = 3, Width = 40, Text = option.Description ?? string.Empty };
        var titleField = new TextField { X = 18, Y = 5, Width = 40, Text = option.Title ?? string.Empty };
        var cwdField = new TextField { X = 18, Y = 7, Width = 40, Text = cwdSeed ?? string.Empty };
        var cmdField = new TextField { X = 18, Y = 9, Width = 40, Text = option.Command ?? string.Empty };

        var isSubmenu = new CheckBox { X = 18, Y = 11, Text = "Has submenu", Value = option.IsSubmenu ? CheckState.Checked : CheckState.UnChecked };

        var okButton = new Button { X = 18, Y = 13, Text = "Ok", IsDefault = true };
        var cancelButton = new Button { X = Pos.Right(okButton) + 2, Y = 13, Text = "Cancel" };

        bool accepted = false;

        void TryOk()
        {
            var nameText = nameField.Text;
            var titleText = titleField.Text;
            var commandText = cmdField.Text;
            var isSubmenuChecked = isSubmenu.Value == CheckState.Checked;

            if (string.IsNullOrWhiteSpace(nameText))
            {
                MessageBox.Query(_app, "Validation", "Name is required.", "Ok");
                return;
            }

            if (isSubmenuChecked && string.IsNullOrWhiteSpace(titleText))
            {
                MessageBox.Query(_app, "Validation", "Title is required for submenus.", "Ok");
                return;
            }

            if (!isSubmenuChecked && string.IsNullOrWhiteSpace(commandText))
            {
                MessageBox.Query(_app, "Validation", "Command is required when not a submenu.", "Ok");
                return;
            }

            OnOk();
        }

        void OnOk()
        {
            option.Name = nameField.Text;
            option.Description = string.IsNullOrWhiteSpace(descField.Text) ? null : descField.Text;
            option.Title = string.IsNullOrWhiteSpace(titleField.Text) ? null : titleField.Text;

            var newCwd = cwdField.Text;
            option.Cwd = string.IsNullOrWhiteSpace(newCwd) ? null : newCwd;

            option.Command = string.IsNullOrWhiteSpace(cmdField.Text) ? null : cmdField.Text;

            if (isSubmenu.Value == CheckState.Checked)
            {
                option.Options ??= new List<DialogOption>();
            }
            else
            {
                option.Options = null;
            }

            accepted = true;
            _app.RequestStop();
            _app.TopRunnableView?.SetNeedsDraw();
        }

        nameField.KeyDown += (_, key) =>
        {
            if (key.KeyCode == Key.Enter.KeyCode)
            {
                key.Handled = true;
                TryOk();
            }
        };
        titleField.KeyDown += (_, key) =>
        {
            if (key.KeyCode == Key.Enter.KeyCode)
            {
                key.Handled = true;
                TryOk();
            }
        };
        cmdField.KeyDown += (_, key) =>
        {
            if (key.KeyCode == Key.Enter.KeyCode)
            {
                key.Handled = true;
                TryOk();
            }
        };
        descField.KeyDown += (_, key) =>
        {
            if (key.KeyCode == Key.Enter.KeyCode)
            {
                key.Handled = true;
                TryOk();
            }
        };
        cwdField.KeyDown += (_, key) =>
        {
            if (key.KeyCode == Key.Enter.KeyCode)
            {
                key.Handled = true;
                TryOk();
            }
        };

        WireButton(okButton, TryOk);
        WireButton(cancelButton, () => { _app.RequestStop(); _app.TopRunnableView?.SetNeedsDraw(); });

        dialog.Add(
            new Label { X = 1, Y = 1, Text = "Name:" },
            nameField,
            new Label { X = 1, Y = 3, Text = "Description:" },
            descField,
            new Label { X = 1, Y = 5, Text = "Title (submenu):" },
            titleField,
            new Label { X = 1, Y = 7, Text = "CWD:" },
            cwdField,
            new Label { X = 1, Y = 9, Text = "Command:" },
            cmdField,
            isSubmenu
        );
        dialog.AddButton(okButton);
        dialog.AddButton(cancelButton);

        _app.Run(dialog);
        _app.TopRunnableView?.SetNeedsDraw();
        dialog.Dispose();
        return accepted;
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

    public static void ShowHelp()
    {
        Console.WriteLine("""
            LSAC Setup - Configuration Wizard
            
            Usage:
              LSAC setup [OPTIONS]
            
            Options:
              -f, --file <path>     Use a custom configuration file path
              -h, --help           Show this help message
            
            Examples:
              LSAC setup                          # Use default config location
              LSAC setup -f ./custom.config       # Use custom config file
            """);
    }
}
