namespace GPack.Core.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public interface ILogger
{
    void Log(LogLevel level, string message, Exception? exception = null);
}

public static class LoggerExtensions
{
    public static void LogInfo(this ILogger logger, string message) => logger.Log(LogLevel.Info, message);
    public static void LogDebug(this ILogger logger, string message) => logger.Log(LogLevel.Debug, message);
    public static void LogWarning(this ILogger logger, string message) => logger.Log(LogLevel.Warning, message);
    public static void LogError(this ILogger logger, string message, Exception? exception = null) => logger.Log(LogLevel.Error, message, exception);
}
