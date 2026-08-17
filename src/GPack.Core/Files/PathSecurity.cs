namespace GPack.Core.Files;

public static class PathSecurity
{
    public static string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Path cannot be empty.", nameof(relativePath));
        }

        string trimmed = relativePath.Trim();

        // Check for absolute, rooted, UNC or drive-letter paths before stripping leading slashes
        if (trimmed.StartsWith('/') || trimmed.StartsWith('\\') || trimmed.Contains(':') || Path.IsPathRooted(trimmed))
        {
            throw new InvalidOperationException($"Security error: Absolute or rooted path is forbidden in package: '{relativePath}'");
        }

        // Normalize separators to Windows standard backslash
        string normalized = trimmed.Replace('/', '\\');

        // Check for traversal components
        var parts = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part == "..")
            {
                throw new InvalidOperationException($"Security error: Path traversal '..' is forbidden: '{relativePath}'");
            }
        }

        return string.Join("\\", parts);
    }

    public static string EnsureSafePath(string baseDirectory, string relativePath)
    {
        var normalizedRelative = NormalizeRelativePath(relativePath);
        var fullBase = Path.GetFullPath(baseDirectory).TrimEnd('\\', '/');
        var fullTarget = Path.GetFullPath(Path.Combine(fullBase, normalizedRelative));

        if (!fullTarget.StartsWith(fullBase + "\\", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fullTarget, fullBase, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Security error: Target path '{fullTarget}' escapes base directory '{fullBase}'");
        }

        return fullTarget;
    }
}
