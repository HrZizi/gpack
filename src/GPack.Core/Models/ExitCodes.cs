namespace GPack.Core.Models;

public static class ExitCodes
{
    public const int Success = 0;

    public const int InvalidArguments = 10;
    public const int InvalidConfiguration = 11;
    public const int SourceNotFound = 12;

    public const int ScanFailed = 20;
    public const int FileReadFailed = 21;
    public const int HashingFailed = 22;

    public const int CompressionFailed = 30;
    public const int PackageCreationFailed = 31;

    public const int InstallerGenerationFailed = 40;

    public const int VerificationFailed = 50;

    public const int InstallationFailed = 60;

    public const int OperationCancelled = 90;
    public const int UnexpectedError = 99;
}
