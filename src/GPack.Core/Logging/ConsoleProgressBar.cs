using GPack.Core.Files;
using GPack.Core.Models;

namespace GPack.Core.Logging;

public class ConsoleProgressBar : IDisposable
{
    private static readonly object ConsoleLock = new();
    private static ConsoleProgressBar? _activeBar;

    private readonly TextWriter _writer;
    private readonly bool _isRedirected;
    private readonly int _minBarWidth;
    private readonly int _maxBarWidth;
    private readonly TimeSpan _minUpdateInterval;

    private DateTime _lastUpdateTime = DateTime.MinValue;
    private int _lastRenderedLength = 0;
    private bool _isDisposed = false;
    private bool _isCompleted = false;

    public static object GlobalLock => ConsoleLock;
    public static ConsoleProgressBar? ActiveBar => _activeBar;

    public ConsoleProgressBar(
        TextWriter? writer = null,
        int minBarWidth = 10,
        int maxBarWidth = 28,
        int updateIntervalMs = 40)
    {
        _writer = writer ?? Console.Out;
        _isRedirected = writer == null ? Console.IsOutputRedirected : false;
        _minBarWidth = minBarWidth;
        _maxBarWidth = maxBarWidth;
        _minUpdateInterval = TimeSpan.FromMilliseconds(updateIntervalMs);

        lock (ConsoleLock)
        {
            _activeBar = this;
        }
    }

    public void Report(InstallProgress progress)
    {
        if (_isDisposed || _isCompleted) return;

        var now = DateTime.UtcNow;
        bool isTerminal = progress.ProgressRatio >= 1.0 || progress.Stage == InstallStage.Finished;

        if (!isTerminal && (now - _lastUpdateTime) < _minUpdateInterval)
        {
            return;
        }

        _lastUpdateTime = now;

        string line = FormatProgressLine(progress, GetConsoleWidth());

        lock (ConsoleLock)
        {
            if (_isRedirected)
            {
                // In redirected mode, only report significant intervals (every 25%) to avoid huge log files
                int pct = (int)(progress.ProgressRatio * 100);
                if (pct % 25 == 0 || isTerminal)
                {
                    _writer.WriteLine($"[{progress.Stage}] {pct}% ({SizeFormatter.FormatBytes(progress.ProcessedBytes)}/{SizeFormatter.FormatBytes(progress.TotalBytes)})");
                }
            }
            else
            {
                _writer.Write("\r" + line);
                _lastRenderedLength = line.Length;
            }
        }
    }

    public static string FormatProgressLine(InstallProgress progress, int totalWidth = 80, bool useUnicode = true)
    {
        string stageTag = $"[{progress.Stage}]";
        double ratio = Math.Clamp(progress.ProgressRatio, 0.0, 1.0);
        int percentage = (int)(ratio * 100);
        string pctStr = $"{percentage,3}%";

        string metrics = "";
        if (progress.TotalBytes > 0)
        {
            metrics = $"({SizeFormatter.FormatBytes(progress.ProcessedBytes)}/{SizeFormatter.FormatBytes(progress.TotalBytes)})";
        }
        else if (progress.ProcessedBytes > 0)
        {
            metrics = $"({SizeFormatter.FormatBytes(progress.ProcessedBytes)})";
        }

        string speedStr = progress.SpeedBytesPerSecond > 0
            ? $"{SizeFormatter.FormatBytes((long)progress.SpeedBytesPerSecond)}/s"
            : "";

        string etaStr = progress.EstimatedRemaining.HasValue
            ? $"ETA: {progress.EstimatedRemaining.Value:mm\\:ss}"
            : "";

        // Build status parts list
        var infoParts = new List<string>();
        if (!string.IsNullOrEmpty(metrics)) infoParts.Add(metrics);
        if (!string.IsNullOrEmpty(speedStr)) infoParts.Add(speedStr);
        if (!string.IsNullOrEmpty(etaStr)) infoParts.Add(etaStr);

        string infoSection = string.Join(" | ", infoParts);
        if (!string.IsNullOrEmpty(infoSection))
        {
            infoSection = " " + infoSection;
        }

        // Calculate available bar width
        // Layout: [Stage] [██████░░░░] 100% (1.2 GB/2.4 GB) | 120 MB/s | ETA: 00:05 | file.txt
        int fixedOverhead = stageTag.Length + 1 + 2 + pctStr.Length + infoSection.Length + 1; // +2 for brackets around bar
        int availableForBarAndFile = Math.Max(0, totalWidth - fixedOverhead - 1);

        int barWidth = Math.Clamp(availableForBarAndFile / 3, 10, 24);
        int filledCount = (int)Math.Round(ratio * barWidth);
        int emptyCount = Math.Max(0, barWidth - filledCount);

        char filledChar = useUnicode ? '█' : '=';
        char emptyChar = useUnicode ? '░' : '-';

        string bar = "[" + new string(filledChar, filledCount) + new string(emptyChar, emptyCount) + "]";

        int remainingSpaceForFile = totalWidth - (stageTag.Length + 1 + bar.Length + 1 + pctStr.Length + infoSection.Length + 3);
        string fileStr = "";

        if (remainingSpaceForFile > 10 && !string.IsNullOrWhiteSpace(progress.CurrentFile))
        {
            string cleanFile = progress.CurrentFile.Replace('\\', '/');
            if (cleanFile.Length > remainingSpaceForFile)
            {
                cleanFile = "..." + cleanFile[^(remainingSpaceForFile - 3)..];
            }
            fileStr = " | " + cleanFile;
        }

        string fullLine = $"{stageTag} {bar} {pctStr}{infoSection}{fileStr}";

        // Pad with trailing spaces to clear any previously rendered longer text
        if (fullLine.Length < totalWidth)
        {
            fullLine = fullLine.PadRight(totalWidth);
        }

        return fullLine;
    }

    public void ClearCurrentLine()
    {
        if (_isRedirected) return;

        lock (ConsoleLock)
        {
            int width = Math.Max(_lastRenderedLength, GetConsoleWidth());
            _writer.Write("\r" + new string(' ', width) + "\r");
            _lastRenderedLength = 0;
        }
    }

    public void Complete(string? finalMessage = null)
    {
        if (_isCompleted) return;
        _isCompleted = true;

        lock (ConsoleLock)
        {
            ClearCurrentLine();
            if (!string.IsNullOrEmpty(finalMessage))
            {
                _writer.WriteLine(finalMessage);
            }
            if (_activeBar == this)
            {
                _activeBar = null;
            }
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            Complete();
        }
    }

    private static int GetConsoleWidth()
    {
        try
        {
            if (!Console.IsOutputRedirected && Console.WindowWidth > 0)
            {
                return Math.Min(Console.WindowWidth - 1, 120);
            }
        }
        catch { }
        return 80;
    }
}
