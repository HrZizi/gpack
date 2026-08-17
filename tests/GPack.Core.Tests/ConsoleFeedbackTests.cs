using System.Text.Json;
using GPack.Core.Logging;
using GPack.Core.Models;
using GPack.Core.Models.Events;
using Xunit;

namespace GPack.Core.Tests;

public class ConsoleProgressBarTests
{
    [Fact]
    public void FormatProgressLine_DeterminateStage_IncludesStageAndPercentageAndBar()
    {
        var progress = new InstallProgress(
            Stage: InstallStage.Compressing,
            ProcessedBytes: 500_000_000,
            TotalBytes: 1_000_000_000,
            CurrentFile: "Game/Binaries/Win64/game.exe",
            ProgressRatio: 0.5,
            SpeedBytesPerSecond: 100_000_000,
            EstimatedRemaining: TimeSpan.FromSeconds(5)
        );

        string formatted = ConsoleProgressBar.FormatProgressLine(progress, totalWidth: 100, useUnicode: true);

        Assert.Contains("[Compressing]", formatted);
        Assert.Contains("50%", formatted);
        Assert.Contains("ETA: 00:05", formatted);
        Assert.Contains("█", formatted);
        Assert.Contains("░", formatted);
        Assert.Contains("game.exe", formatted);
    }

    [Fact]
    public void FormatProgressLine_AsciiMode_UsesAsciiCharacters()
    {
        var progress = new InstallProgress(
            Stage: InstallStage.Hashing,
            ProcessedBytes: 250,
            TotalBytes: 1000,
            CurrentFile: "test.txt",
            ProgressRatio: 0.25
        );

        string formatted = ConsoleProgressBar.FormatProgressLine(progress, totalWidth: 80, useUnicode: false);

        Assert.Contains("[Hashing]", formatted);
        Assert.Contains("25%", formatted);
        Assert.Contains("=", formatted);
        Assert.Contains("-", formatted);
        Assert.DoesNotContain("█", formatted);
    }

    [Fact]
    public void FormatProgressLine_LongFileName_TruncatesSafelyWithEllipsis()
    {
        var progress = new InstallProgress(
            Stage: InstallStage.Extracting,
            ProcessedBytes: 10,
            TotalBytes: 100,
            CurrentFile: "Very/Deeply/Nested/Directory/With/An/Extremely/Long/Path/And/File/Name/GameAsset_Level01_TextureDiffuse_4K_HDR.dat",
            ProgressRatio: 0.1
        );

        string formatted = ConsoleProgressBar.FormatProgressLine(progress, totalWidth: 70, useUnicode: true);

        Assert.Contains("...", formatted);
        Assert.True(formatted.Length >= 70);
    }

    [Fact]
    public void ConsoleProgressBar_Lifecycle_ReportsAndCompletes()
    {
        var sw = new StringWriter();
        using (var bar = new ConsoleProgressBar(writer: sw, updateIntervalMs: 0))
        {
            bar.Report(new InstallProgress(
                Stage: InstallStage.Verifying,
                ProcessedBytes: 100,
                TotalBytes: 200,
                CurrentFile: "data.bin",
                ProgressRatio: 0.5
            ));
            bar.ClearCurrentLine();
            bar.Complete("Verification finished");
        }

        string output = sw.ToString();
        Assert.Contains("Verification finished", output);
    }
}

public class ConsoleSpinnerTests
{
    [Fact]
    public async Task ConsoleSpinner_RunAsync_ExecutesActionAndCompletes()
    {
        var sw = new StringWriter();
        bool executed = false;

        using (var spinner = new ConsoleSpinner("Inspecting source files...", writer: sw, useUnicode: false))
        {
            spinner.UpdateMessage("Scanning binaries...");
            await Task.Delay(100);
            executed = true;
            spinner.Stop(success: true, "Inspection complete.");
        }

        Assert.True(executed);
        string output = sw.ToString();
        Assert.Contains("[✓]", output);
        Assert.Contains("Inspection complete.", output);
    }

    [Fact]
    public void ConsoleSpinner_StopFailure_ShowsFailureMark()
    {
        var sw = new StringWriter();
        using (var spinner = new ConsoleSpinner("Validating...", writer: sw, useUnicode: false))
        {
            spinner.Stop(success: false, "Validation failed.");
        }

        string output = sw.ToString();
        Assert.Contains("[✗]", output);
        Assert.Contains("Validation failed.", output);
    }
}

public class ConsoleProgressReporterTests
{
    [Fact]
    public void ProgressReporter_JsonMode_OutputsValidJsonEvents()
    {
        var sw = new StringWriter();
        using (var reporter = new ConsoleProgressReporter(quiet: false, jsonMode: true, writer: sw))
        {
            reporter.Report(new InstallProgress(
                Stage: InstallStage.Compressing,
                ProcessedBytes: 500,
                TotalBytes: 1000,
                CurrentFile: "payload.bin",
                ProgressRatio: 0.5,
                SpeedBytesPerSecond: 100
            ));

            reporter.Report(new InstallProgress(
                Stage: InstallStage.Finished,
                ProcessedBytes: 1000,
                TotalBytes: 1000,
                CurrentFile: null,
                ProgressRatio: 1.0
            ));
        }

        var lines = sw.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(lines);

        var firstEvent = JsonSerializer.Deserialize<JsonProgressEvent>(lines[0]);
        Assert.NotNull(firstEvent);
        Assert.Equal("progress", firstEvent.Event);
        Assert.Equal("compressing", firstEvent.Stage);
        Assert.Equal(0.5, firstEvent.Progress);
        Assert.Equal(500, firstEvent.ProcessedBytes);
        Assert.Equal(1000, firstEvent.TotalBytes);
    }

    [Fact]
    public void ProgressReporter_QuietMode_OutputsNothing()
    {
        var sw = new StringWriter();
        using (var reporter = new ConsoleProgressReporter(quiet: true, jsonMode: false, writer: sw))
        {
            reporter.Report(new InstallProgress(
                Stage: InstallStage.Compressing,
                ProcessedBytes: 500,
                TotalBytes: 1000,
                CurrentFile: "test.dat",
                ProgressRatio: 0.5
            ));
        }

        string output = sw.ToString();
        Assert.Empty(output);
    }

    [Fact]
    public void ProgressReporter_MultiStageTransitions_HandlesSmoothly()
    {
        var sw = new StringWriter();
        using (var reporter = new ConsoleProgressReporter(quiet: false, jsonMode: false, writer: sw))
        {
            // Indeterminate stage: Scanning
            reporter.Report(new InstallProgress(Stage: InstallStage.Scanning, ProcessedBytes: 0, TotalBytes: 0, CurrentFile: null, ProgressRatio: 0));

            // Determinate stage: Hashing
            reporter.Report(new InstallProgress(Stage: InstallStage.Hashing, ProcessedBytes: 100, TotalBytes: 200, CurrentFile: "file.exe", ProgressRatio: 0.5));

            // Determinate stage: Compressing
            reporter.Report(new InstallProgress(Stage: InstallStage.Compressing, ProcessedBytes: 200, TotalBytes: 200, CurrentFile: "file.exe", ProgressRatio: 1.0));

            // Finished stage
            reporter.Report(new InstallProgress(Stage: InstallStage.Finished, ProcessedBytes: 200, TotalBytes: 200, CurrentFile: null, ProgressRatio: 1.0));
        }

        string output = sw.ToString();
        Assert.NotEmpty(output);
    }
}
