using System.Text.RegularExpressions;
using GPack.Abstractions;

namespace GPack.Installer.Core;

public class VariableResolver
{
    private readonly Dictionary<string, string> _variables = new(StringComparer.OrdinalIgnoreCase);

    public VariableResolver(
        string installDirectory,
        string packageDirectory,
        string tempDirectory)
    {
        _variables["%INSTALL_DIR%"] = Path.GetFullPath(installDirectory).TrimEnd('\\', '/');
        _variables["%PACKAGE_DIR%"] = Path.GetFullPath(packageDirectory).TrimEnd('\\', '/');
        _variables["%TEMP_DIR%"] = Path.GetFullPath(tempDirectory).TrimEnd('\\', '/');
        _variables["%USER_PROFILE%"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _variables["%APPDATA%"] = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _variables["%LOCALAPPDATA%"] = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _variables["%PROGRAMDATA%"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        _variables["%DESKTOP%"] = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        _variables["%START_MENU%"] = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        _variables["%PROGRAMS%"] = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        
        // %GAMES% default resolution: D:\Games or C:\Games
        string gamesDir = "C:\\Games";
        try
        {
            var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed).ToList();
            var dDrive = drives.FirstOrDefault(d => d.Name.StartsWith("D", StringComparison.OrdinalIgnoreCase));
            if (dDrive != null)
            {
                gamesDir = "D:\\Games";
            }
        }
        catch { }
        _variables["%GAMES%"] = gamesDir;
    }

    public void SetVariable(string name, string value)
    {
        string key = name.StartsWith("%") && name.EndsWith("%") ? name : $"%{name}%";
        _variables[key] = value;
    }

    public string Resolve(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        string result = input;
        foreach (var (key, val) in _variables)
        {
            result = Regex.Replace(result, Regex.Escape(key), val, RegexOptions.IgnoreCase);
        }

        // Also expand standard OS environment variables like %TEMP%
        result = Environment.ExpandEnvironmentVariables(result);
        return result;
    }
}

public class InstallContext : IInstallContext
{
    private readonly VariableResolver _variableResolver;
    private readonly Action<string>? _logAction;

    public string InstallDirectory { get; }
    public string PackageDirectory { get; }
    public string TempDirectory { get; }

    public InstallContext(
        string installDirectory,
        string packageDirectory,
        string tempDirectory,
        Action<string>? logAction = null)
    {
        InstallDirectory = installDirectory;
        PackageDirectory = packageDirectory;
        TempDirectory = tempDirectory;
        _logAction = logAction;
        _variableResolver = new VariableResolver(installDirectory, packageDirectory, tempDirectory);
    }

    public string ResolveVariables(string input) => _variableResolver.Resolve(input);

    public void Log(string message) => _logAction?.Invoke(message);
}
