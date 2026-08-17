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

    [Fact]
    public async Task IsoPackager_CreatesValidIsoWithFiles()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "gpack-iso-test-" + Guid.NewGuid().ToString("N"));
        string sourceDir = Path.Combine(tempDir, "SourceFolder");
        string isoPath = Path.Combine(tempDir, "output.iso");

        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(Path.Combine(sourceDir, "data"));
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "Setup.exe"), "setup-binary");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "manifest.gpack"), "{\"test\":1}");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "data", "payload-000.gpk"), "gpk-payload-data");

        try
        {
            var packager = new GPack.Builder.Packaging.IsoPackager();
            var resultPath = await packager.CreateIsoAsync(sourceDir, isoPath, "TEST_VOL");

            Assert.True(File.Exists(resultPath));
            var fi = new FileInfo(resultPath);
            Assert.True(fi.Length > 0);

            // Read ISO back using CDReader to verify file contents
            using var stream = File.OpenRead(resultPath);
            using var cd = new DiscUtils.Iso9660.CDReader(stream, joliet: true);
            Assert.True(cd.FileExists("Setup.exe"));
            Assert.True(cd.FileExists("manifest.gpack"));
            Assert.True(cd.FileExists(@"data\payload-000.gpk") || cd.FileExists("data/payload-000.gpk"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task ZipPackager_CreatesValidZipWithFiles()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "gpack-zip-test-" + Guid.NewGuid().ToString("N"));
        string sourceDir = Path.Combine(tempDir, "SourceFolder");
        string zipPath = Path.Combine(tempDir, "output.zip");

        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(Path.Combine(sourceDir, "data"));
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "Setup.exe"), "setup-binary");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "manifest.gpack"), "{\"test\":1}");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "data", "payload-000.gpk"), "gpk-payload-data");

        try
        {
            var packager = new GPack.Builder.Packaging.ZipPackager();
            var resultPath = await packager.CreateZipAsync(sourceDir, zipPath);

            Assert.True(File.Exists(resultPath));
            var fi = new FileInfo(resultPath);
            Assert.True(fi.Length > 0);

            // Read ZIP back using ZipArchive to verify file entries
            using var archive = System.IO.Compression.ZipFile.OpenRead(resultPath);
            Assert.NotNull(archive.GetEntry("Setup.exe"));
            Assert.NotNull(archive.GetEntry("manifest.gpack"));
            Assert.NotNull(archive.GetEntry("data/payload-000.gpk"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task BuildEngine_WithIsoFormat_ProducesIsoArtifactAndCleansStaging()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "gpack-engine-iso-test-" + Guid.NewGuid().ToString("N"));
        string sourceDir = Path.Combine(tempDir, "SourceGame");
        string outputDir = Path.Combine(tempDir, "OutputRepacks");

        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "Game.exe"), "executable-binary-data");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "data.pak"), new string('Z', 20000));

        try
        {
            var config = new BuildConfiguration
            {
                Game = new GameConfig { Name = "IsoGame", Version = "1.0.0" },
                Source = new SourceConfig { Directory = sourceDir },
                Launch = new LaunchConfig { Executable = "Game.exe" },
                Package = new PackageConfig
                {
                    Format = "iso",
                    Compression = "fast",
                    OutputDirectory = outputDir
                }
            };

            var engine = new BuildEngine();
            var result = await engine.BuildAsync(config);

            Assert.True(result.Success);
            Assert.Equal(GPack.Core.Models.PackageOutputFormat.Iso, result.OutputFormat);
            Assert.NotNull(result.OutputArtifactPath);
            Assert.EndsWith(".iso", result.OutputArtifactPath, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(result.OutputArtifactPath));

            // Verify staging folders cleaned up
            var stagingFolders = Directory.GetDirectories(outputDir, ".gpack-staging-*");
            Assert.Empty(stagingFolders);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task BuildEngine_WithZipFormat_ProducesZipArtifact()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "gpack-engine-zip-test-" + Guid.NewGuid().ToString("N"));
        string sourceDir = Path.Combine(tempDir, "SourceGame");
        string outputDir = Path.Combine(tempDir, "OutputRepacks");

        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "Game.exe"), "executable-binary-data");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "data.pak"), new string('Y', 20000));

        try
        {
            var config = new BuildConfiguration
            {
                Game = new GameConfig { Name = "ZipGame", Version = "1.0.0" },
                Source = new SourceConfig { Directory = sourceDir },
                Launch = new LaunchConfig { Executable = "Game.exe" },
                Package = new PackageConfig
                {
                    Format = "zip",
                    Compression = "fast",
                    OutputDirectory = outputDir
                }
            };

            var engine = new BuildEngine();
            var result = await engine.BuildAsync(config);

            Assert.True(result.Success);
            Assert.Equal(GPack.Core.Models.PackageOutputFormat.Zip, result.OutputFormat);
            Assert.NotNull(result.OutputArtifactPath);
            Assert.EndsWith(".zip", result.OutputArtifactPath, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(result.OutputArtifactPath));

            // Verify manifest can be read from zip
            var manifest = await GPack.Installer.Core.PackageReader.LoadManifestAsync(result.OutputArtifactPath);
            Assert.Equal("ZipGame", manifest.Game.Name);
            Assert.Equal("Game.exe", manifest.Launch.Executable);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task BuildEngine_IsoWithLargeSplitSize_AutomaticallyCapsTo2GB()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "gpack-iso-cap-test-" + Guid.NewGuid().ToString("N"));
        string sourceDir = Path.Combine(tempDir, "SourceGame");
        string outputDir = Path.Combine(tempDir, "OutputRepacks");

        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "Game.exe"), "executable-binary-data");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "data.pak"), "content");

        try
        {
            var config = new BuildConfiguration
            {
                Game = new GameConfig { Name = "CapTestGame", Version = "1.0.0" },
                Source = new SourceConfig { Directory = sourceDir },
                Launch = new LaunchConfig { Executable = "Game.exe" },
                Package = new PackageConfig
                {
                    Format = "iso",
                    SplitSize = "8GB",
                    SplitSizeBytes = 8L * 1024 * 1024 * 1024,
                    OutputDirectory = outputDir
                }
            };

            var engine = new BuildEngine();
            var result = await engine.BuildAsync(config);

            Assert.True(result.Success);
            Assert.Equal(GPack.Core.Models.PackageOutputFormat.Iso, result.OutputFormat);
            Assert.Equal(GPack.Core.Packaging.PackageRechunker.IsoSafeMaxChunkSize, config.Package.SplitSizeBytes);
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


