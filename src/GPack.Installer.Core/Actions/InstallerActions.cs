using System.Diagnostics;
using System.Runtime.InteropServices;
using GPack.Abstractions;
using GPack.Core.Logging;
using GPack.Core.Manifest;
using Microsoft.Win32;

namespace GPack.Installer.Core.Actions;

public class CopyAction : IInstallerAction
{
    public string ActionType => "copy";
    public string Source { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;

    public Task ExecuteAsync(IInstallContext context, CancellationToken cancellationToken = default)
    {
        string src = context.ResolveVariables(Source);
        string dst = context.ResolveVariables(Destination);

        if (File.Exists(src))
        {
            var dir = Path.GetDirectoryName(dst);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.Copy(src, dst, overwrite: true);
            context.Log($"Copied file: {src} -> {dst}");
        }
        else if (Directory.Exists(src))
        {
            Directory.CreateDirectory(dst);
            foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(src, file);
                string targetFile = Path.Combine(dst, rel);
                var targetDir = Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);
                File.Copy(file, targetFile, overwrite: true);
            }
            context.Log($"Copied directory: {src} -> {dst}");
        }
        else
        {
            context.Log($"Warning: Copy source not found: {src}");
        }

        return Task.CompletedTask;
    }
}

public class MoveAction : IInstallerAction
{
    public string ActionType => "move";
    public string Source { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;

    public Task ExecuteAsync(IInstallContext context, CancellationToken cancellationToken = default)
    {
        string src = context.ResolveVariables(Source);
        string dst = context.ResolveVariables(Destination);

        if (File.Exists(src))
        {
            var dir = Path.GetDirectoryName(dst);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (File.Exists(dst)) File.Delete(dst);
            File.Move(src, dst);
            context.Log($"Moved file: {src} -> {dst}");
        }
        else if (Directory.Exists(src))
        {
            if (Directory.Exists(dst)) Directory.Delete(dst, recursive: true);
            Directory.Move(src, dst);
            context.Log($"Moved directory: {src} -> {dst}");
        }
        else
        {
            context.Log($"Warning: Move source not found: {src}");
        }

        return Task.CompletedTask;
    }
}

public class DeleteAction : IInstallerAction
{
    public string ActionType => "delete";
    public string Target { get; set; } = string.Empty;

    public Task ExecuteAsync(IInstallContext context, CancellationToken cancellationToken = default)
    {
        string target = context.ResolveVariables(Target);

        if (File.Exists(target))
        {
            File.Delete(target);
            context.Log($"Deleted file: {target}");
        }
        else if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
            context.Log($"Deleted directory: {target}");
        }

        return Task.CompletedTask;
    }
}

public class CommandAction : IInstallerAction
{
    public string ActionType => "command";
    public string Executable { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string? WorkingDirectory { get; set; }
    public bool Wait { get; set; } = true;

    public async Task ExecuteAsync(IInstallContext context, CancellationToken cancellationToken = default)
    {
        string exe = context.ResolveVariables(Executable);
        string args = context.ResolveVariables(Arguments);
        string? workDir = !string.IsNullOrWhiteSpace(WorkingDirectory) ? context.ResolveVariables(WorkingDirectory) : null;

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            WorkingDirectory = workDir ?? Path.GetDirectoryName(exe) ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        context.Log($"Running command: {exe} {args}");
        using var process = Process.Start(psi);
        if (process == null)
        {
            context.Log($"Failed to start process: {exe}");
            return;
        }

        if (Wait)
        {
            await process.WaitForExitAsync(cancellationToken);
            context.Log($"Command finished with exit code {process.ExitCode}");
        }
    }
}

public class ShortcutAction : IInstallerAction
{
    public string ActionType => "shortcut";
    public string Target { get; set; } = string.Empty;
    public string Location { get; set; } = "desktop"; // "desktop" | "startmenu" | custom path
    public string? Name { get; set; }
    public string? Arguments { get; set; }
    public string? IconLocation { get; set; }

    public Task ExecuteAsync(IInstallContext context, CancellationToken cancellationToken = default)
    {
        var shortcutManager = new ShortcutManager();
        string target = context.ResolveVariables(Target);
        string? args = !string.IsNullOrEmpty(Arguments) ? context.ResolveVariables(Arguments) : null;
        string? icon = !string.IsNullOrEmpty(IconLocation) ? context.ResolveVariables(IconLocation) : null;
        string name = Name ?? Path.GetFileNameWithoutExtension(target);

        if (string.Equals(Location, "desktop", StringComparison.OrdinalIgnoreCase))
        {
            shortcutManager.CreateDesktopShortcut(name, target, args, iconLocation: icon);
        }
        else if (string.Equals(Location, "startmenu", StringComparison.OrdinalIgnoreCase))
        {
            shortcutManager.CreateStartMenuShortcut(name, target, arguments: args, iconLocation: icon);
        }
        else
        {
            string resolvedPath = context.ResolveVariables(Location);
            shortcutManager.CreateShortcut(resolvedPath, target, args, iconLocation: icon);
        }

        return Task.CompletedTask;
    }
}

public class RegistryAction : IInstallerAction
{
    public string ActionType => "registry";
    public string KeyPath { get; set; } = string.Empty;
    public string ValueName { get; set; } = string.Empty;
    public string ValueData { get; set; } = string.Empty;
    public string ValueType { get; set; } = "String"; // String | DWord | QWord | Binary

    public Task ExecuteAsync(IInstallContext context, CancellationToken cancellationToken = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return Task.CompletedTask;

        string keyPath = context.ResolveVariables(KeyPath);
        string valName = context.ResolveVariables(ValueName);
        string valData = context.ResolveVariables(ValueData);

        try
        {
            RegistryKey? baseKey = null;
            string subKey = keyPath;

            if (keyPath.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase) || keyPath.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase))
            {
                baseKey = Registry.CurrentUser;
                subKey = keyPath.Substring(keyPath.IndexOf('\\') + 1);
            }
            else if (keyPath.StartsWith("HKEY_LOCAL_MACHINE\\", StringComparison.OrdinalIgnoreCase) || keyPath.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase))
            {
                baseKey = Registry.LocalMachine;
                subKey = keyPath.Substring(keyPath.IndexOf('\\') + 1);
            }
            else
            {
                baseKey = Registry.CurrentUser;
            }

            using var reg = baseKey.CreateSubKey(subKey, writable: true);
            if (reg != null)
            {
                var kind = ValueType.ToLowerInvariant() switch
                {
                    "dword" => RegistryValueKind.DWord,
                    "qword" => RegistryValueKind.QWord,
                    "binary" => RegistryValueKind.Binary,
                    _ => RegistryValueKind.String
                };

                object dataToSet = kind switch
                {
                    RegistryValueKind.DWord => int.Parse(valData),
                    RegistryValueKind.QWord => long.Parse(valData),
                    _ => valData
                };

                reg.SetValue(valName, dataToSet, kind);
                context.Log($"Set registry value: [{keyPath}] \"{valName}\"=\"{valData}\"");
            }
        }
        catch (Exception ex)
        {
            context.Log($"Failed to write registry key: {ex.Message}");
        }

        return Task.CompletedTask;
    }
}

public class EnvironmentVariableAction : IInstallerAction
{
    public string ActionType => "env";
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;

    public Task ExecuteAsync(IInstallContext context, CancellationToken cancellationToken = default)
    {
        string name = context.ResolveVariables(Name);
        string value = context.ResolveVariables(Value);

        try
        {
            Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
            context.Log($"Set user environment variable: {name}={value}");
        }
        catch (Exception ex)
        {
            context.Log($"Failed to set environment variable: {ex.Message}");
        }

        return Task.CompletedTask;
    }
}

public static class ActionExecutor
{
    public static async Task ExecuteActionsAsync(
        IEnumerable<ActionDefinition> actions,
        IInstallContext context,
        CancellationToken cancellationToken = default)
    {
        foreach (var def in actions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IInstallerAction? action = def.Type.ToLowerInvariant() switch
            {
                "copy" => new CopyAction { Source = def.Source ?? "", Destination = def.Destination ?? "" },
                "move" => new MoveAction { Source = def.Source ?? "", Destination = def.Destination ?? "" },
                "delete" => new DeleteAction { Target = def.Target ?? "" },
                "command" => new CommandAction { Executable = def.Executable ?? "", Arguments = def.Arguments ?? "", WorkingDirectory = def.WorkingDirectory, Wait = def.Wait },
                "shortcut" => new ShortcutAction { Target = def.Target ?? "", Location = def.Location ?? "desktop", Name = def.Name, Arguments = def.Arguments },
                "registry" => new RegistryAction { KeyPath = def.KeyPath ?? "", ValueName = def.ValueName ?? "", ValueData = def.ValueData ?? "", ValueType = def.ValueType ?? "String" },
                "env" => new EnvironmentVariableAction { Name = def.Name ?? "", Value = def.Value ?? "" },
                _ => null
            };

            if (action != null)
            {
                await action.ExecuteAsync(context, cancellationToken);
            }
            else
            {
                context.Log($"Warning: Unknown action type '{def.Type}'");
            }
        }
    }
}
