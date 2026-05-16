namespace LSAC;

using System.Diagnostics;
using System.Text;
using System.Runtime.InteropServices;

public static class Utils
{
    public static string? ResolveCwd(string? parentCwd, string? currentCwd)
    {
        if (string.IsNullOrWhiteSpace(currentCwd))
        {
            return string.IsNullOrWhiteSpace(parentCwd) ? null : parentCwd;
        }

        if (Path.IsPathRooted(currentCwd))
        {
            return currentCwd;
        }

        if (!string.IsNullOrWhiteSpace(parentCwd))
        {
            return Path.GetFullPath(Path.Combine(parentCwd, currentCwd));
        }

        return Path.GetFullPath(currentCwd);
    }

    public static CommandResult ExecuteShellCommand(string command, string? cwd)
    {
        var shell = GetShellInvocation(command);
        var output = new StringBuilder();
        var error = new StringBuilder();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = shell.FileName,
                Arguments = shell.Arguments,
                WorkingDirectory = GetValidCwd(cwd),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data != null)
            {
                output.AppendLine(args.Data);
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data != null)
            {
                error.AppendLine(args.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        return new CommandResult(process.ExitCode, output.ToString().TrimEnd(), error.ToString().TrimEnd());
    }

    public static int ExecuteShellCommandInteractive(string command, string? cwd)
    {
        var shell = GetShellInvocation(command);
        var startInfo = new ProcessStartInfo
        {
            FileName = shell.FileName,
            Arguments = shell.Arguments,
            UseShellExecute = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            CreateNoWindow = false
        };

        var workingDirectory = GetValidCwd(cwd);
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        process.WaitForExit();
        return process.ExitCode;
    }

    public static void ApplyConsoleForInteractive()
    {
        if (!OperatingSystem.IsWindows())
        {
            RestoreUnixTerminal();
            return;
        }

        if (!TryGetConsoleMode(InputHandle, out var inputMode))
        {
            return;
        }

        inputMode |= ConsoleModeFlags.EnableProcessedInput | ConsoleModeFlags.EnableLineInput | ConsoleModeFlags.EnableEchoInput;
        inputMode &= ~ConsoleModeFlags.EnableMouseInput;
        inputMode &= ~ConsoleModeFlags.EnableVirtualTerminalInput;
        SetConsoleMode(InputHandle, inputMode);

        ApplyOutputConsoleMode(enableVirtualTerminal: true);
    }

    public static void ApplyConsoleForTui()
    {
        if (!OperatingSystem.IsWindows())
        {
            RestoreUnixTerminal();
            return;
        }

        if (!TryGetConsoleMode(InputHandle, out var inputMode))
        {
            return;
        }

        inputMode |= ConsoleModeFlags.EnableProcessedInput | ConsoleModeFlags.EnableVirtualTerminalInput;
        inputMode &= ~(ConsoleModeFlags.EnableLineInput | ConsoleModeFlags.EnableEchoInput);
        SetConsoleMode(InputHandle, inputMode);

        ApplyOutputConsoleMode(enableVirtualTerminal: true);
        FlushInputBuffer();
    }

    public static void DrainConsoleInput()
    {
        if (OperatingSystem.IsWindows())
        {
            FlushInputBuffer();
            return;
        }

        try
        {
            while (Console.KeyAvailable)
            {
                Console.ReadKey(true);
            }
        }
        catch
        {
            // Ignore if console input is not available.
        }
    }

    public static void DisableMouseReporting()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        WriteEscapeSequence("\u001b[?1000l\u001b[?1002l\u001b[?1003l\u001b[?1006l\u001b[?1015l");
    }

    public static void EnableMouseReporting()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        WriteEscapeSequence("\u001b[?1000h\u001b[?1002h\u001b[?1003h\u001b[?1006h\u001b[?1015h");
    }

    public static void ExitAlternateScreen()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        WriteEscapeSequence("\u001b[?1049l");
    }

    public static void EnterAlternateScreen()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        WriteEscapeSequence("\u001b[?1049h");
    }

    private static void WriteEscapeSequence(string sequence)
    {
        try
        {
            Console.Write(sequence);
            Console.Out.Flush();
        }
        catch
        {
            // Ignore if console output is not available.
        }
    }

    private static void RestoreUnixTerminal()
    {
        if (OperatingSystem.IsWindows()) return;

        var sttyArgs = string.IsNullOrWhiteSpace(_savedTerminalState) ? "sane" : _savedTerminalState;
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    Arguments = $"-c \"stty {sttyArgs} < /dev/tty\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.WaitForExit();
        }
        catch { }
    }

    private static string? GetValidCwd(string? cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd))
        {
            return null;
        }

        return Directory.Exists(cwd) ? cwd : null;
    }

    private static (string FileName, string Arguments) GetShellInvocation(string command)
    {
        if (OperatingSystem.IsWindows())
        {
            return ("cmd.exe", $"/c {command}");
        }

        var escaped = command.Replace("\"", "\\\"");
        return ("/bin/bash", $"-lc \"{escaped}\"");
    }

    private static string? _savedTerminalState;

    public static void SaveTerminalState()
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    Arguments = "-c \"stty -g < /dev/tty\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            _savedTerminalState = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
        }
        catch { }
    }

    private static readonly IntPtr InputHandle = OperatingSystem.IsWindows() ? GetStdHandle(StdInputHandle) : IntPtr.Zero;
    private static readonly IntPtr OutputHandle = OperatingSystem.IsWindows() ? GetStdHandle(StdOutputHandle) : IntPtr.Zero;

    private static bool TryGetConsoleMode(IntPtr handle, out ConsoleModeFlags mode)
    {
        if (!GetConsoleMode(handle, out var rawMode))
        {
            mode = default;
            return false;
        }

        mode = (ConsoleModeFlags)rawMode;
        return true;
    }

    private const int StdInputHandle = -10;
    private const int StdOutputHandle = -11;

    [Flags]
    private enum ConsoleModeFlags : uint
    {
        EnableEchoInput = 0x0004,
        EnableInsertMode = 0x0020,
        EnableLineInput = 0x0002,
        EnableMouseInput = 0x0010,
        EnableProcessedInput = 0x0001,
        EnableQuickEditMode = 0x0040,
        EnableVirtualTerminalInput = 0x0200
    }

    [Flags]
    private enum ConsoleOutputModeFlags : uint
    {
        EnableProcessedOutput = 0x0001,
        EnableWrapAtEolOutput = 0x0002,
        EnableVirtualTerminalProcessing = 0x0004,
        DisableNewlineAutoReturn = 0x0008
    }

    private static void ApplyOutputConsoleMode(bool enableVirtualTerminal)
    {
        if (!TryGetConsoleOutputMode(out var outputMode))
        {
            return;
        }

        outputMode |= ConsoleOutputModeFlags.EnableProcessedOutput | ConsoleOutputModeFlags.EnableWrapAtEolOutput;
        if (enableVirtualTerminal)
        {
            outputMode |= ConsoleOutputModeFlags.EnableVirtualTerminalProcessing;
        }
        else
        {
            outputMode &= ~ConsoleOutputModeFlags.EnableVirtualTerminalProcessing;
        }

        SetConsoleMode(OutputHandle, outputMode);
    }

    private static bool TryGetConsoleOutputMode(out ConsoleOutputModeFlags mode)
    {
        if (!GetConsoleMode(OutputHandle, out var rawMode))
        {
            mode = default;
            return false;
        }

        mode = (ConsoleOutputModeFlags)rawMode;
        return true;
    }

    private static void FlushInputBuffer()
    {
        FlushConsoleInputBuffer(InputHandle);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, ConsoleModeFlags dwMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, ConsoleOutputModeFlags dwMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FlushConsoleInputBuffer(IntPtr hConsoleInput);
}

public sealed record CommandResult(int ExitCode, string Output, string Error);
