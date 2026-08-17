namespace GPack.Core.Logging;

public class ConsoleSpinner : IDisposable
{
    private static readonly string[] BrailleFrames = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
    private static readonly string[] AsciiFrames = { "-", "\\", "|", "/" };
    private static readonly object ConsoleLock = ConsoleProgressBar.GlobalLock;
    private static ConsoleSpinner? _activeSpinner;

    private readonly TextWriter _writer;
    private readonly bool _isRedirected;
    private readonly bool _useUnicode;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task? _spinTask;

    private string _message;
    private int _frameIndex = 0;
    private int _lastRenderedLength = 0;
    private bool _isDisposed = false;
    private bool _isStopped = false;

    public static ConsoleSpinner? ActiveSpinner => _activeSpinner;

    public ConsoleSpinner(string initialMessage = "", TextWriter? writer = null, bool useUnicode = true)
    {
        _message = initialMessage;
        _writer = writer ?? Console.Out;
        _isRedirected = writer == null ? Console.IsOutputRedirected : false;
        _useUnicode = useUnicode;

        lock (ConsoleLock)
        {
            _activeSpinner = this;
        }

        if (!_isRedirected)
        {
            _spinTask = Task.Run(SpinLoopAsync);
        }
        else if (!string.IsNullOrWhiteSpace(_message))
        {
            _writer.WriteLine($"[*] {_message}");
        }
    }

    public static ConsoleSpinner Start(string initialMessage = "", TextWriter? writer = null)
    {
        return new ConsoleSpinner(initialMessage, writer);
    }

    public static async Task RunAsync(string message, Func<ConsoleSpinner, Task> action)
    {
        using var spinner = Start(message);
        try
        {
            await action(spinner);
            spinner.Stop(success: true);
        }
        catch (Exception)
        {
            spinner.Stop(success: false);
            throw;
        }
    }

    public static async Task<T> RunAsync<T>(string message, Func<ConsoleSpinner, Task<T>> action)
    {
        using var spinner = Start(message);
        try
        {
            var result = await action(spinner);
            spinner.Stop(success: true);
            return result;
        }
        catch (Exception)
        {
            spinner.Stop(success: false);
            throw;
        }
    }

    public void UpdateMessage(string newMessage)
    {
        _message = newMessage;
        if (_isRedirected && !string.IsNullOrWhiteSpace(newMessage))
        {
            _writer.WriteLine($"[*] {newMessage}");
        }
    }

    private async Task SpinLoopAsync()
    {
        var frames = _useUnicode ? BrailleFrames : AsciiFrames;

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                RenderFrame(frames[_frameIndex]);
                _frameIndex = (_frameIndex + 1) % frames.Length;

                await Task.Delay(80, _cts.Token);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void RenderFrame(string frame)
    {
        if (_isRedirected || _isStopped || _isDisposed) return;

        lock (ConsoleLock)
        {
            int totalWidth = GetConsoleWidth();
            string line = $"{frame} {_message}";

            if (line.Length > totalWidth && totalWidth > 4)
            {
                line = line[..(totalWidth - 3)] + "...";
            }

            if (line.Length < _lastRenderedLength)
            {
                line = line.PadRight(_lastRenderedLength);
            }

            _writer.Write("\r" + line);
            _lastRenderedLength = line.Length;
        }
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

    public void Stop(bool success = true, string? finalMessage = null)
    {
        if (_isStopped) return;
        _isStopped = true;
        _cts.Cancel();

        try
        {
            _spinTask?.Wait(200);
        }
        catch { }

        lock (ConsoleLock)
        {
            ClearCurrentLine();

            string statusSymbol = success ? "[✓]" : "[✗]";
            string message = finalMessage ?? _message;

            if (!string.IsNullOrWhiteSpace(message))
            {
                _writer.WriteLine($"{statusSymbol} {message}");
            }

            if (_activeSpinner == this)
            {
                _activeSpinner = null;
            }
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            Stop(success: true);
            _cts.Dispose();
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
