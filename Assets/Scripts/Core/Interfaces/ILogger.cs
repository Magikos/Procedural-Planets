public enum LogLevel
{
    Trace,
    Debug,
    Info,
    Warning,
    Error
}

public interface ILogger
{
    void Log(LogLevel level, string system, string message);
    void LogException(string system, System.Exception ex);
}
