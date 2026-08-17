namespace GPack.Core.Logging;

public class ConsoleLogger : ILogger
{
    private readonly bool _quiet;
    private readonly bool _verbose;
    private readonly bool _jsonMode;

    public ConsoleLogger(bool quiet = false, bool verbose = false, bool jsonMode = false)
    {
        _quiet = quiet;
        _verbose = verbose;
        _jsonMode = jsonMode;
    }

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        if (_jsonMode) return;
        if (_quiet && level < LogLevel.Warning) return;
        if (!_verbose && level == LogLevel.Debug) return;

        lock (ConsoleProgressBar.GlobalLock)
        {
            // Clear current active line if a spinner or progress bar is rendering
            ConsoleProgressBar.ActiveBar?.ClearCurrentLine();
            ConsoleSpinner.ActiveSpinner?.ClearCurrentLine();

            var prevColor = Console.ForegroundColor;
            switch (level)
            {
                case LogLevel.Debug:
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    break;
                case LogLevel.Info:
                    Console.ForegroundColor = ConsoleColor.Gray;
                    break;
                case LogLevel.Warning:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    break;
                case LogLevel.Error:
                    Console.ForegroundColor = ConsoleColor.Red;
                    break;
            }

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            Console.WriteLine($"[{timestamp}] [{level.ToString().ToUpperInvariant()}] {message}");
            if (exception != null && (_verbose || level == LogLevel.Error))
            {
                Console.WriteLine(exception.ToString());
            }

            Console.ForegroundColor = prevColor;
        }
    }
}

public class FileLogger : ILogger, IDisposable
{
    private readonly StreamWriter? _writer;
    private readonly object _lock = new();

    public string LogFilePath { get; }

    public FileLogger(string? customPath = null, string appName = "NagelRepacks")
    {
        try
        {
            if (string.IsNullOrWhiteSpace(customPath))
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var logDir = Path.Combine(localAppData, appName, "Logs");
                Directory.CreateDirectory(logDir);
                LogFilePath = Path.Combine(logDir, $"{DateTime.Now:yyyy-MM-dd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}.log");
            }
            else
            {
                var dir = Path.GetDirectoryName(customPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                LogFilePath = customPath;
            }

            _writer = new StreamWriter(new FileStream(LogFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                AutoFlush = true
            };
        }
        catch
        {
            LogFilePath = string.Empty;
        }
    }

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        if (_writer == null) return;
        lock (_lock)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                _writer.WriteLine($"{timestamp} [{level.ToString().ToUpperInvariant()}] {message}");
                if (exception != null)
                {
                    _writer.WriteLine(exception.ToString());
                }
            }
            catch { }
        }
    }

    public void Dispose()
    {
        _writer?.Dispose();
    }
}

public class MultiLogger : ILogger
{
    private readonly List<ILogger> _loggers = new();

    public MultiLogger(params ILogger[] loggers)
    {
        _loggers.AddRange(loggers.Where(l => l != null));
    }

    public void Add(ILogger logger)
    {
        if (logger != null) _loggers.Add(logger);
    }

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        foreach (var logger in _loggers)
        {
            logger.Log(level, message, exception);
        }
    }
}
