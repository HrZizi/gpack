namespace GPack.Core.Models;

public enum InstallStage
{
    Idle,
    Preparing,
    CheckingPackage,
    CheckingDiskSpace,
    CreatingTemporaryEnvironment,
    Scanning,
    Hashing,
    Compressing,
    Extracting,
    Verifying,
    ExecutingActions,
    CreatingShortcuts,
    Finalizing,
    Finished,
    RollingBack,
    Cancelled,
    Failed
}

public record InstallProgress(
    InstallStage Stage,
    long ProcessedBytes,
    long TotalBytes,
    string? CurrentFile,
    double ProgressRatio,
    double SpeedBytesPerSecond = 0,
    TimeSpan? EstimatedRemaining = null
);
