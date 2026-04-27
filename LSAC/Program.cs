using LSAC;
using LSAC.UserInterface;
using Terminal.Gui;
using Terminal.Gui.App;

// Handle command-line arguments
if (args.Length > 0)
{
    string command = args[0].ToLower();

    if (command == "setup")
    {
        HandleSetupCommand(args);
        return;
    }
    else if (command is "-h" or "--help" or "help")
    {
        UiSetup.ShowHelp();
        return;
    }
    else
    {
        Console.WriteLine($"Unknown command: {command}");
        UiSetup.ShowHelp();
        return;
    }
}

// Normal application flow
if (!Config.LoadConfig())
{
    Console.WriteLine($"Config not found at '{Config.GetDefaultSavePath()}'. Run 'LSAC setup' to create one.");
    return;
}

if (Config.Model.Dialog == null)
{
    Console.WriteLine("Config loaded but no dialog is configured. Run 'LSAC setup' to update it.");
    return;
}

using IApplication app = Application.Create();
app.Init();

var ui = new UiBuilder(app, Config.Model);
ui.Run();

static void HandleSetupCommand(string[] args)
{
    string? customConfigPath = null;

    // Parse arguments
    for (int i = 1; i < args.Length; i++)
    {
        if (args[i] is "-f" or "--file")
        {
            if (i + 1 < args.Length)
            {
                customConfigPath = args[i + 1];
                i++;
            }
            else
            {
                Console.WriteLine("Error: -f/--file requires a file path");
                UiSetup.ShowHelp();
                return;
            }
        }
        else if (args[i] is "-h" or "--help")
        {
            UiSetup.ShowHelp();
            return;
        }
        else
        {
            Console.WriteLine($"Unknown option: {args[i]}");
            UiSetup.ShowHelp();
            return;
        }
    }

    // Load existing config if available
    if (customConfigPath != null && File.Exists(customConfigPath))
    {
        Config.LoadConfig(customConfigPath);
    }
    else if (customConfigPath == null && File.Exists(Config.GetDefaultSavePath()))
    {
        Config.LoadConfig();
    }

    using IApplication app = Application.Create();
    app.Init();

    var setupUi = new UiSetup(app);
    setupUi.RunSetupWizard();
}
