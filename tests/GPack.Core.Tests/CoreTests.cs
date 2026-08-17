using GPack.Core.Configuration;
using GPack.Core.Files;
using GPack.Core.Hashing;
using GPack.Core.Manifest;
using Xunit;

namespace GPack.Core.Tests;

public class PathSecurityTests
{
    [Theory]
    [InlineData("game.exe", "game.exe")]
    [InlineData("Data/data.pak", "Data\\data.pak")]
    [InlineData("Engine/Binaries/x64/engine.dll", "Engine\\Binaries\\x64\\engine.dll")]
    [InlineData("a/b/c/d.txt", "a\\b\\c\\d.txt")]
    public void NormalizeRelativePath_ValidPaths_NormalizesCorrectly(string input, string expected)
    {
        var result = PathSecurity.NormalizeRelativePath(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("Data/../../escape.txt")]
    [InlineData("..\\Windows\\System32\\cmd.exe")]
    public void NormalizeRelativePath_PathTraversal_ThrowsInvalidOperationException(string malicious)
    {
        Assert.Throws<InvalidOperationException>(() => PathSecurity.NormalizeRelativePath(malicious));
    }

    [Theory]
    [InlineData("C:\\Windows\\System32\\cmd.exe")]
    [InlineData("/etc/passwd")]
    [InlineData("\\\\server\\share\\file.txt")]
    public void NormalizeRelativePath_RootedOrAbsolutePaths_ThrowsInvalidOperationException(string absolutePath)
    {
        Assert.Throws<InvalidOperationException>(() => PathSecurity.NormalizeRelativePath(absolutePath));
    }

    [Fact]
    public void EnsureSafePath_SafePath_CombinesSuccessfully()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "test-base");
        var result = PathSecurity.EnsureSafePath(baseDir, "subdir/file.txt");
        Assert.Equal(Path.GetFullPath(Path.Combine(baseDir, "subdir", "file.txt")), result);
    }
}

public class GlobMatcherTests
{
    [Theory]
    [InlineData("*.log", "game.log", true)]
    [InlineData("*.log", "logs/game.log", true)]
    [InlineData("*.log", "game.exe", false)]
    [InlineData("CrashDumps/**", "CrashDumps/dump1.dmp", true)]
    [InlineData("CrashDumps/**", "CrashDumps/sub/dump2.dmp", true)]
    [InlineData("CrashDumps/**", "Game/dump.dmp", false)]
    [InlineData("*.tmp", "data/temp.tmp", true)]
    public void GlobMatcher_MatchesCorrectly(string pattern, string path, bool expected)
    {
        var matcher = new GlobMatcher(new[] { pattern });
        Assert.Equal(expected, matcher.IsMatch(path));
    }
}

public class SizeFormatterTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(1073741824, "1 GB")]
    [InlineData(4294967296, "4 GB")]
    public void FormatBytes_FormatsExpected(long bytes, string expected)
    {
        var result = SizeFormatter.FormatBytes(bytes);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("500MB", 500L * 1024 * 1024)]
    [InlineData("4GB", 4L * 1024 * 1024 * 1024)]
    [InlineData("2.5GB", (long)(2.5 * 1024 * 1024 * 1024))]
    [InlineData("1024KB", 1024L * 1024)]
    public void ParseSize_ParsesExpected(string input, long expected)
    {
        var result = SizeFormatter.ParseSize(input);
        Assert.Equal(expected, result);
    }
}

public class ConfigurationMergerTests
{
    [Fact]
    public void Merge_OverridesDefaultValues()
    {
        var baseConfig = ConfigurationMerger.CreateDefault();
        var overrideConfig = new BuildConfiguration
        {
            Game = new GameConfig { Name = "Cyberpunk 2077", Version = "2.3" },
            Package = new PackageConfig { Compression = "maximum", SplitSize = "2GB", SplitSizeBytes = 2L * 1024 * 1024 * 1024 }
        };

        var merged = ConfigurationMerger.Merge(baseConfig, overrideConfig);

        Assert.Equal("Cyberpunk 2077", merged.Game.Name);
        Assert.Equal("2.3", merged.Game.Version);
        Assert.Equal("maximum", merged.Package.Compression);
        Assert.Equal(2L * 1024 * 1024 * 1024, merged.Package.SplitSizeBytes);
        Assert.Equal("NagelRepacks", merged.Installer.Brand); // Retained default
    }
}

public class Sha256HashProviderTests
{
    [Fact]
    public async Task ComputeStreamHashAsync_KnownString_MatchesKnownHash()
    {
        var hasher = new Sha256HashProvider();
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("hello nagelrepacks"));
        var hash = await hasher.ComputeStreamHashAsync(stream);

        // Computed sha256 for "hello nagelrepacks"
        using var sha = System.Security.Cryptography.SHA256.Create();
        var expected = Convert.ToHexStringLower(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes("hello nagelrepacks")));

        Assert.Equal(expected, hash);
    }
}

public class ManifestSerializationTests
{
    [Fact]
    public void Manifest_RoundTrip_PreservesAllFields()
    {
        var manifest = new PackageManifest
        {
            FormatVersion = 1,
            Package = new PackageMetadata
            {
                Generator = "gpack",
                GeneratorVersion = "0.1.0",
                Brand = "NagelRepacks",
                CreatedUtc = DateTime.UtcNow
            },
            Game = new GameMetadata
            {
                Name = "Test Game",
                Version = "1.0.0"
            },
            Launch = new LaunchConfiguration
            {
                Executable = "bin/game.exe",
                Arguments = "-fullscreen"
            },
            Payload = new PayloadMetadata
            {
                Compression = "zstd",
                CompressionLevel = 10,
                UncompressedSize = 123456,
                TotalFiles = 2,
                Chunks = new List<PackageChunk>
                {
                    new PackageChunk { Index = 0, File = "data/payload-000.gpk", Size = 50000, Sha256 = "abc" }
                }
            },
            Files = new List<FileEntry>
            {
                new FileEntry { Path = "bin\\game.exe", Size = 100000, Sha256 = "123" },
                new FileEntry { Path = "data\\data.pak", Size = 23456, Sha256 = "456" }
            }
        };

        var json = ManifestSerializer.Serialize(manifest);
        var deserialized = ManifestSerializer.Deserialize(json);

        Assert.Equal(1, deserialized.FormatVersion);
        Assert.Equal("Test Game", deserialized.Game.Name);
        Assert.Equal("bin/game.exe", deserialized.Launch.Executable);
        Assert.Equal(2, deserialized.Files.Count);
        Assert.Single(deserialized.Payload.Chunks);
        Assert.Equal("abc", deserialized.Payload.Chunks[0].Sha256);
    }
}
