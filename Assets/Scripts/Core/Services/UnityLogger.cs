using System;
using UnityEngine;

public class UnityLogger : ILogger
{
    LogLevel _minLevel;

    public UnityLogger(LogLevel minLevel = LogLevel.Debug)
    {
        _minLevel = minLevel;
    }

    public void Log(LogLevel level, string system, string message)
    {
        if (level < _minLevel) return;

        string formatted = $"[{system}] {message}";
        switch (level)
        {
            case LogLevel.Trace:
            case LogLevel.Debug:
            case LogLevel.Info:
                UnityEngine.Debug.Log(formatted);
                break;
            case LogLevel.Warning:
                UnityEngine.Debug.LogWarning(formatted);
                break;
            case LogLevel.Error:
                UnityEngine.Debug.LogError(formatted);
                break;
        }
    }

    public void LogException(string system, Exception ex)
    {
        UnityEngine.Debug.LogError($"[{system}] Exception: {ex.Message}\n{ex.StackTrace}");
    }
}

public static class LoggerProvider
{
    static ILogger _fallbackLogger;

    public static ILogger Get()
    {
        if (_fallbackLogger != null)
            return _fallbackLogger;

        if (ServiceLocator.TryGet(out _fallbackLogger))
            return _fallbackLogger;

        return _fallbackLogger = ServiceLocator.Register<ILogger>(new UnityLogger());
    }

    public static void Log(LogLevel level, string system, string message)
    {
        Get().Log(level, system, message);
    }

    public static void LogException(string system, Exception ex)
    {
        Get().LogException(system, ex);
    }
}
