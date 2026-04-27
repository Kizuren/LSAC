namespace LSAC;

public class Logger : ILogger
{
    public static ILogger[] Listeners { get; set; } = [];

    public void Log(LogLevel logLevel, string message)
    {
        foreach (var listener in Listeners)
        {
            listener.Log(logLevel, message);
        }
    }

    public void Debug(string message)
    {
        Log(LogLevel.Debug, message);
    }

    public void Info(string message)
    {
        Log(LogLevel.Info, message);
    }

    public void Warn(string message)
    {
        Log(LogLevel.Warning, message);
    }

    public void Error(string message)
    {
        Log(LogLevel.Error, message);
    }
}

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public interface ILogger
{
    void Log(LogLevel logLevel, string message);
}