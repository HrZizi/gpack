using GPack.Builder;
using GPack.Core.Configuration;
using GPack.Core.Logging;
using Xunit;

namespace GPack.Builder.Tests;

public class BuilderTests
{
    [Fact]
    public async Task PackageBuilder_BuildsValidChunksAndManifest()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "gpack-builder-test-" + Guid.NewGuid().ToString("N"));
        string sourceDir = Path.Combine(tempDir, "SourceGame");
        string outputDir = Path.Combine(tempDir, "OutputRepack");

        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(Path.Combine(sourceDir, "Data"));

        // Create mock files
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "Game.exe"), "executable-binary-data");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "Data", "content.pak"), new string('A', 50000));
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "Data", "music.mp3"), new string('B', 30000));
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "debug.log"), "debug log text");

        try
        {
            var config = new BuildConfiguration
            {
                Game = new GameConfig { Name = "TestGame", Version = "1.0.0" },
                Source = new SourceConfig
                {
                    Directory = sourceDir,
                    Exclude = new List<string> { "*.log" }
                },
                Launch = new LaunchConfig { Executable = "Game.exe" },
                Package = new PackageConfig
                {
                    Compression = "balanced",
                    SplitSizeBytes = 40000 // Test chunk splitting (should create ~2-3 chunks)
                }
            };

            var builder = new PackageBuilder(new ConsoleLogger());
            var result = await builder.BuildPackageAsync(config, outputDir);

            Assert.True(result.Success);
            Assert.True(File.Exists(result.ManifestFilePath));
            Assert.Equal(3, result.Manifest.Files.Count); // Game.exe, content.pak, music.mp3 (debug.log excluded)
            Assert.DoesNotContain(result.Manifest.Files, f => f.Path.EndsWith(".log"));
            Assert.True(result.Manifest.Payload.Chunks.Count >= 1);

            foreach (var chunk in result.Manifest.Payload.Chunks)
            {
                var chunkPath = Path.Combine(outputDir, chunk.File.Replace('/', '\\'));
                Assert.True(File.Exists(chunkPath));
                Assert.True(chunk.Size > 0);
                Assert.False(string.IsNullOrEmpty(chunk.Sha256));
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }
}
