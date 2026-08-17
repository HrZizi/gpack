using System.Runtime.InteropServices;
using GPack.Core.Logging;

namespace GPack.Installer.Core;

public class ShortcutManager
{
    private readonly ILogger _logger;

    public ShortcutManager(ILogger? logger = null)
    {
        _logger = logger ?? new ConsoleLogger();
    }

    public bool CreateShortcut(
        string shortcutPath,
        string targetPath,
        string? arguments = null,
        string? workingDirectory = null,
        string? iconLocation = null,
        string? description = null)
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _logger.LogWarning("Shortcut creation is only supported on Windows.");
                return false;
            }

            var dir = Path.GetDirectoryName(shortcutPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                _logger.LogWarning("WScript.Shell ProgID not found.");
                return false;
            }

            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell == null) return false;

            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = Path.GetFullPath(targetPath);

            if (!string.IsNullOrWhiteSpace(arguments))
                shortcut.Arguments = arguments;

            if (!string.IsNullOrWhiteSpace(workingDirectory))
                shortcut.WorkingDirectory = Path.GetFullPath(workingDirectory);
            else
                shortcut.WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(targetPath));

            if (!string.IsNullOrWhiteSpace(iconLocation))
                shortcut.IconLocation = iconLocation;

            if (!string.IsNullOrWhiteSpace(description))
                shortcut.Description = description;

            shortcut.Save();
            _logger.LogInfo($"Shortcut created: {shortcutPath} -> {targetPath}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to create shortcut at '{shortcutPath}': {ex.Message}", ex);
            return false;
        }
    }

    public bool CreateDesktopShortcut(
        string shortcutName,
        string targetPath,
        string? arguments = null,
        string? workingDirectory = null,
        string? iconLocation = null,
        string? description = null)
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string shortcutPath = Path.Combine(desktop, $"{shortcutName}.lnk");
        return CreateShortcut(shortcutPath, targetPath, arguments, workingDirectory, iconLocation, description);
    }

    public bool CreateStartMenuShortcut(
        string shortcutName,
        string targetPath,
        string? subFolder = null,
        string? arguments = null,
        string? workingDirectory = null,
        string? iconLocation = null,
        string? description = null)
    {
        string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        string targetDir = !string.IsNullOrWhiteSpace(subFolder) ? Path.Combine(programs, subFolder) : programs;
        string shortcutPath = Path.Combine(targetDir, $"{shortcutName}.lnk");
        return CreateShortcut(shortcutPath, targetPath, arguments, workingDirectory, iconLocation, description);
    }
}
