using System.Text.RegularExpressions;

namespace GPack.Core.Files;

public class GlobMatcher
{
    private readonly List<Regex> _patterns = new();

    public GlobMatcher(IEnumerable<string>? patterns = null)
    {
        if (patterns != null)
        {
            foreach (var p in patterns)
            {
                AddPattern(p);
            }
        }
    }

    public void AddPattern(string glob)
    {
        if (string.IsNullOrWhiteSpace(glob)) return;

        var regex = GlobToRegex(glob.Trim());
        _patterns.Add(regex);
    }

    public bool IsMatch(string relativePath)
    {
        if (_patterns.Count == 0) return false;
        string normalized = relativePath.Replace('\\', '/').Trim('/');

        foreach (var pattern in _patterns)
        {
            if (pattern.IsMatch(normalized)) return true;
        }

        return false;
    }

    private static Regex GlobToRegex(string glob)
    {
        string normalized = glob.Replace('\\', '/').Trim('/');
        bool hasSlash = normalized.Contains('/');

        string regexPattern = Regex.Escape(normalized)
            .Replace(@"\*\*", ".*")
            .Replace(@"\*", "[^/]*")
            .Replace(@"\?", "[^/]");

        if (!hasSlash)
        {
            return new Regex($"(^|/){regexPattern}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return new Regex($"^{regexPattern}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
