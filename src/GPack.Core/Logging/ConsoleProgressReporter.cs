using System.Text.Json;
using GPack.Core.Files;
using GPack.Core.Models;
using GPack.Core.Models.Events;

namespace GPack.Core.Logging;

public class ConsoleProgressReporter : IProgress<InstallProgress>, IDisposable
{
    private readonly bool _quiet;
    private readonly bool _jsonMode;
    private readonly TextWriter _writer;

    private ConsoleProgressBar? _progressBar;
    private ConsoleSpinner? _spinner;
    private InstallStage? _currentStage;
    private DateTime _lastJsonReport = DateTime.MinValue;
    private bool _isDisposed = false;

    public ConsoleProgressReporter(bool quiet = false, bool jsonMode = false, TextWriter? writer = null)
    {
        _quiet = quiet;
        _jsonMode = jsonMode;
        _writer = writer ?? Console.Out;
    }

    public void Report(InstallProgress p)
    {
        if (_isDisposed) return;

        if (_jsonMode)
        {
            ReportJson(p);
            return;
        }

        if (_quiet) return;

        bool stageChanged = _currentStage != p.Stage;
        _currentStage = p.Stage;

        if (IsDeterminateStage(p.Stage))
        {
            if (_spinner != null)
            {
                _spinner.Stop(success: true);
                _spinner.Dispose();
                _spinner = null;
            }

            if (_progressBar == null || stageChanged)
            {
                _progressBar?.Dispose();
                _progressBar = new ConsoleProgressBar(_writer);
            }

            _progressBar.Report(p);
        }
        else if (p.Stage == InstallStage.Finished)
        {
            _spinner?.Stop(success: true, "Finished successfully.");
            _spinner?.Dispose();
            _spinner = null;

            _progressBar?.Complete();
            _progressBar?.Dispose();
            _progressBar = null;
        }
        else if (p.Stage == InstallStage.Failed || p.Stage == InstallStage.Cancelled)
        {
            _spinner?.Stop(success: false, p.Stage == InstallStage.Cancelled ? "Operation cancelled." : "Operation failed.");
            _spinner?.Dispose();
            _spinner = null;

            _progressBar?.Complete();
            _progressBar?.Dispose();
            _progressBar = null;
        }
        else // Indeterminate stage
        {
            _progressBar?.Complete();
            _progressBar?.Dispose();
            _progressBar = null;

            string stageDescription = GetStageDescription(p.Stage, p.CurrentFile);

            if (_spinner == null)
            {
                _spinner = new ConsoleSpinner(stageDescription, _writer);
            }
            else
            {
                _spinner.UpdateMessage(stageDescription);
            }
        }
    }

    private void ReportJson(InstallProgress p)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastJsonReport).TotalMilliseconds >= 100 || p.ProgressRatio >= 1.0)
        {
            _lastJsonReport = now;
            var evt = new JsonProgressEvent
            {
                Event = "progress",
                Stage = p.Stage.ToString().ToLowerInvariant(),
                Progress = Math.Round(p.ProgressRatio, 4),
                ProcessedBytes = p.ProcessedBytes,
                TotalBytes = p.TotalBytes,
                CurrentFile = p.CurrentFile,
                SpeedBytesPerSec = p.SpeedBytesPerSecond
            };
            _writer.WriteLine(JsonSerializer.Serialize(evt));
        }
    }

    private static bool IsDeterminateStage(InstallStage stage)
    {
        return stage switch
        {
            InstallStage.Hashing => true,
            InstallStage.Compressing => true,
            InstallStage.Extracting => true,
            InstallStage.Verifying => true,
            InstallStage.Finalizing => true,
            _ => false
        };
    }

    private static string GetStageDescription(InstallStage stage, string? detail)
    {
        string detailStr = !string.IsNullOrEmpty(detail) ? $" ({detail})" : "";
        return stage switch
        {
            InstallStage.Scanning => $"Scanning source files{detailStr}...",
            InstallStage.Preparing => $"Preparing package manifest{detailStr}...",
            InstallStage.CheckingPackage => $"Validating package payload chunks{detailStr}...",
            InstallStage.CheckingDiskSpace => $"Checking disk space availability{detailStr}...",
            InstallStage.CreatingTemporaryEnvironment => $"Creating isolated staging environment{detailStr}...",
            InstallStage.ExecutingActions => $"Executing post-install actions{detailStr}...",
            InstallStage.CreatingShortcuts => $"Creating system shortcuts{detailStr}...",
            _ => $"{stage}{detailStr}..."
        };
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            _spinner?.Dispose();
            _spinner = null;
            _progressBar?.Dispose();
            _progressBar = null;
        }
    }
}
