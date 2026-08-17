using GPack.Builder;
using GPack.Core.Configuration;
using GPack.Core.Hashing;
using GPack.Core.Logging;
using GPack.Core.Manifest;
using GPack.Core.Models;
using GPack.Installer.Core;
using Xunit;

namespace GPack.Installer.Tests;

public class EndToEndPackagingAndInstallerTests
{
    [Fact]
    public async Task BuildAndInstall_RoundTrip_RestoresIdenticalFiles()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "gpack-e2e-test-" + Guid.NewGuid().ToString("N"));
        string sourceDir = Path.Combine(tempDir, "SourceGame");
        string repackDir = Path.Combine(tempDir, "RepackOutput");
        string installDir = Path.Combine(tempDir, "InstalledGame");

        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(Path.Combine(sourceDir, "Data", "Shaders"));

        // Create sample game files
        string exeContent = "MZ_EXE_HEADER_CONTENT_MOCK_GAME";
        string shaderContent = "ps_5_0 main() { return float4(1,0,0,1); }";
        byte[] binaryData = new byte[150000];
        new Random(42).NextBytes(binaryData);

        await File.WriteAllTextAsync(Path.Combine(sourceDir, "MainGame.exe"), exeContent);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "Data", "Shaders", "pixel.cso"), shaderContent);
        await File.WriteAllBytesAsync(Path.Combine(sourceDir, "Data", "textures.bin"), binaryData);

        try
        {
            // 1. Build Package
            var config = new BuildConfiguration
            {
                Game = new GameConfig { Name = "E2E Game", Version = "1.0.0" },
                Source = new SourceConfig { Directory = sourceDir },
                Launch = new LaunchConfig { Executable = "MainGame.exe" },
                Package = new PackageConfig
                {
                    Compression = "balanced",
                    SplitSizeBytes = 60000 // Test multi-chunk split
                },
                Actions = new List<ActionDefinition>
                {
                    new ActionDefinition
                    {
                        Type = "copy",
                        Source = "%INSTALL_DIR%\\MainGame.exe",
                        Destination = "%INSTALL_DIR%\\MainGame_Backup.exe"
                    }
                }
            };

            var builder = new PackageBuilder(new ConsoleLogger(quiet: true));
            var buildResult = await builder.BuildPackageAsync(config, repackDir);

            Assert.True(buildResult.Success);
            Assert.True(buildResult.Manifest.Payload.Chunks.Count >= 2);

            // 2. Install Package using InstallerEngine
            var engine = new InstallerEngine(new ConsoleLogger(quiet: true));
            var installOptions = new InstallOptions
            {
                PackagePathOrDirectory = repackDir,
                DestinationDirectory = installDir,
                CreateDesktopShortcut = false, // disable shortcut creation in headless unit test
                CreateStartMenuShortcut = false,
                SkipIntegrityCheck = false
            };

            var installResult = await engine.InstallAsync(installOptions);
            Assert.True(installResult.Success);
            Assert.True(Directory.Exists(installDir));

            // 3. Verify extracted file contents
            var hasher = new Sha256HashProvider();

            string extractedExe = Path.Combine(installDir, "MainGame.exe");
            string extractedShader = Path.Combine(installDir, "Data", "Shaders", "pixel.cso");
            string extractedBin = Path.Combine(installDir, "Data", "textures.bin");
            string backupExe = Path.Combine(installDir, "MainGame_Backup.exe");

            Assert.True(File.Exists(extractedExe));
            Assert.True(File.Exists(extractedShader));
            Assert.True(File.Exists(extractedBin));
            Assert.True(File.Exists(backupExe)); // Post-install copy action verified

            Assert.Equal(exeContent, await File.ReadAllTextAsync(extractedExe));
            Assert.Equal(shaderContent, await File.ReadAllTextAsync(extractedShader));
            Assert.Equal(await hasher.ComputeFileHashAsync(Path.Combine(sourceDir, "Data", "textures.bin")),
                         await hasher.ComputeFileHashAsync(extractedBin));
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
    public async Task InstallAsync_WhenCancelled_CleansUpStagingDirectory()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "gpack-cancel-test-" + Guid.NewGuid().ToString("N"));
        string sourceDir = Path.Combine(tempDir, "SourceGame");
        string repackDir = Path.Combine(tempDir, "RepackOutput");
        string installDir = Path.Combine(tempDir, "InstalledGame");

        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "Game.exe"), "test");

        try
        {
            var config = new BuildConfiguration
            {
                Game = new GameConfig { Name = "CancelGame", Version = "1.0.0" },
                Source = new SourceConfig { Directory = sourceDir },
                Launch = new LaunchConfig { Executable = "Game.exe" },
                Package = new PackageConfig { Compression = "fast" }
            };

            var builder = new PackageBuilder(new ConsoleLogger(quiet: true));
            await builder.BuildPackageAsync(config, repackDir);

            var engine = new InstallerEngine(new ConsoleLogger(quiet: true));
            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            var installOptions = new InstallOptions
            {
                PackagePathOrDirectory = repackDir,
                DestinationDirectory = installDir
            };

            var result = await engine.InstallAsync(installOptions, cancellationToken: cts.Token);
            Assert.False(result.Success);

            // Verify no staging folders leaked in parent directory
            var leakedStaging = Directory.GetDirectories(tempDir, ".gpack-install-*");
            Assert.Empty(leakedStaging);
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
    public async Task LoadManifestAsync_FromZipAndIso_LoadsManifestSuccessfully()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "gpack-reader-test-" + Guid.NewGuid().ToString("N"));
        string sourceDir = Path.Combine(tempDir, "SourceGame");
        string zipOut = Path.Combine(tempDir, "game.zip");
        string isoOut = Path.Combine(tempDir, "game.iso");

        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "Game.exe"), "game");

        try
        {
            var config = new BuildConfiguration
            {
                Game = new GameConfig { Name = "ReaderTestGame", Version = "2.0.0" },
                Source = new SourceConfig { Directory = sourceDir },
                Launch = new LaunchConfig { Executable = "Game.exe" },
                Package = new PackageConfig { Compression = "none", OutputDirectory = tempDir }
            };

            // Build ZIP
            config.Package.Format = "zip";
            var engine = new BuildEngine(new ConsoleLogger(quiet: true));
            var zipResult = await engine.BuildAsync(config);
            Assert.True(zipResult.Success);
            Assert.NotNull(zipResult.OutputArtifactPath);

            var zipManifest = await PackageReader.LoadManifestAsync(zipResult.OutputArtifactPath);
            Assert.Equal("ReaderTestGame", zipManifest.Game.Name);
            Assert.Equal("2.0.0", zipManifest.Game.Version);

            // Build ISO
            config.Package.Format = "iso";
            var isoResult = await engine.BuildAsync(config);
            Assert.True(isoResult.Success);
            Assert.NotNull(isoResult.OutputArtifactPath);

            var isoManifest = await PackageReader.LoadManifestAsync(isoResult.OutputArtifactPath);
            Assert.Equal("ReaderTestGame", isoManifest.Game.Name);
            Assert.Equal("2.0.0", isoManifest.Game.Version);
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
    public async Task ConvertCommand_ConvertsBetweenZipAndIsoAndDirectory()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "gpack-convert-test-" + Guid.NewGuid().ToString("N"));
        string sourceDir = Path.Combine(tempDir, "SourceGame");
        string outDir = Path.Combine(tempDir, "Outputs");

        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "Game.exe"), "executable-data");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "content.dat"), "game-assets");

        try
        {
            // 1. Build initial repack as ZIP
            var config = new BuildConfiguration
            {
                Game = new GameConfig { Name = "ConvertGame", Version = "1.2.0" },
                Source = new SourceConfig { Directory = sourceDir },
                Launch = new LaunchConfig { Executable = "Game.exe" },
                Package = new PackageConfig { Format = "zip", OutputDirectory = outDir }
            };

            var engine = new BuildEngine(new ConsoleLogger(quiet: true));
            var zipResult = await engine.BuildAsync(config);
            Assert.True(zipResult.Success);
            string zipPath = zipResult.OutputArtifactPath!;
            Assert.True(File.Exists(zipPath));

            // 2. Convert ZIP -> ISO
            var logger = new ConsoleLogger(quiet: true);
            int convertToIsoExit = await GPack.Cli.Commands.ConvertCommand.ExecuteAsync(
                new[] { zipPath, "--format", "iso", "--output", outDir }, logger);
            Assert.Equal(ExitCodes.Success, convertToIsoExit);

            string expectedIso = Path.Combine(outDir, "ConvertGame-NagelRepacks.iso");
            Assert.True(File.Exists(expectedIso));

            var isoManifest = await PackageReader.LoadManifestAsync(expectedIso);
            Assert.Equal("ConvertGame", isoManifest.Game.Name);
            Assert.Equal("1.2.0", isoManifest.Game.Version);

            // 3. Convert ISO -> Directory
            int convertToDirExit = await GPack.Cli.Commands.ConvertCommand.ExecuteAsync(
                new[] { expectedIso, "--format", "directory", "--output", Path.Combine(outDir, "ExtractedFolder") }, logger);
            Assert.Equal(ExitCodes.Success, convertToDirExit);

            string expectedDir = Path.Combine(outDir, "ExtractedFolder", "ConvertGame-NagelRepacks");
            Assert.True(Directory.Exists(expectedDir));
            Assert.True(File.Exists(Path.Combine(expectedDir, "ConvertGame.gpack")));
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


