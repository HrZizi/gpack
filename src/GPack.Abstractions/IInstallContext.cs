namespace GPack.Abstractions;

public interface IInstallContext
{
    string InstallDirectory { get; }
    string PackageDirectory { get; }
    string TempDirectory { get; }
    string ResolveVariables(string input);
    void Log(string message);
}
