using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GPack.Core.Files;
using GPack.Core.Logging;
using GPack.Core.Manifest;
using GPack.Core.Models;
using GPack.Installer.Core;
using Microsoft.Win32;

namespace GPack.Installer.UI.ViewModels;

public enum UIState
{
    Ready,
    Installing,
    Completed,
    Error
}

public class MainViewModel : INotifyPropertyChanged
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly InstallerEngine _engine;
    private readonly FileLogger _fileLogger;
    private CancellationTokenSource? _cts;
    private PackageManifest? _manifest;
    private string _packageDir = string.Empty;
    private string? _executablePath;

    private UIState _currentState = UIState.Ready;
    private string _brand = "NAGELREPACKS";
    private string _gameName = "LOADING...";
    private string _version = "1.0.0";
    private string _destinationDirectory = string.Empty;
    private string _requiredSpaceText = "--";
    private string _availableSpaceText = "--";
    private bool _hasSufficientSpace = true;
    private bool _createDesktopShortcut = true;
    private bool _createStartMenuShortcut = true;

    private ImageSource? _artworkImage;
    private double _progressRatio = 0;
    private int _progressPercentage = 0;
    private string _currentStageText = "READY TO INSTALL";
    private string _currentFileText = string.Empty;
    private string _extractedProgressText = "0 B / --";
    private string _speedAndEtaText = string.Empty;
    private string _errorMessage = string.Empty;

    public MainViewModel()
    {
        _fileLogger = new FileLogger(appName: "NagelRepacks");
        _engine = new InstallerEngine(_fileLogger);

        LoadDefaultArtwork();

        BrowseDirectoryCommand = new RelayCommand(BrowseDirectory, () => IsReadyState);
        PrimaryActionCommand = new RelayCommand(ExecutePrimaryAction, CanExecutePrimaryAction);
        CancelInstallCommand = new RelayCommand(CancelInstall, () => IsInstallingState);
        LaunchGameCommand = new RelayCommand(LaunchGame, () => IsCompletedState);
        OpenInstallDirectoryCommand = new RelayCommand(OpenInstallDirectory);
        OpenLogsCommand = new RelayCommand(OpenLogs);
        CloseCommand = new RelayCommand(CloseApplication);
        MinimizeCommand = new RelayCommand(MinimizeWindow);
    }

    public UIState CurrentState
    {
        get => _currentState;
        set
        {
            _currentState = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsReadyState));
            OnPropertyChanged(nameof(IsInstallingState));
            OnPropertyChanged(nameof(IsCompletedState));
            OnPropertyChanged(nameof(IsErrorState));
            OnPropertyChanged(nameof(PrimaryButtonText));
            OnPropertyChanged(nameof(StatusHeaderUpper));
            OnPropertyChanged(nameof(StatusDetailLine));
        }
    }

    public bool IsReadyState => CurrentState == UIState.Ready;
    public bool IsInstallingState => CurrentState == UIState.Installing;
    public bool IsCompletedState => CurrentState == UIState.Completed;
    public bool IsErrorState => CurrentState == UIState.Error;

    public string Brand
    {
        get => _brand;
        set { _brand = value; OnPropertyChanged(); }
    }

    public string GameName
    {
        get => _gameName;
        set
        {
            _gameName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayGameName));
        }
    }

    public string DisplayGameName => !string.IsNullOrWhiteSpace(_gameName) ? _gameName.ToUpperInvariant() : string.Empty;

    public string Version
    {
        get => _version;
        set
        {
            _version = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VersionText));
        }
    }

    public string VersionText => string.IsNullOrWhiteSpace(_version) ? "v1.0.0" : (_version.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? _version : $"v{_version}");

    public string DestinationDirectory
    {
        get => _destinationDirectory;
        set
        {
            _destinationDirectory = value;
            OnPropertyChanged();
            UpdateDiskSpace();
        }
    }

    public string RequiredSpaceText
    {
        get => _requiredSpaceText;
        set
        {
            _requiredSpaceText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusDetailLine));
        }
    }

    public string AvailableSpaceText
    {
        get => _availableSpaceText;
        set { _availableSpaceText = value; OnPropertyChanged(); }
    }

    public bool HasSufficientSpace
    {
        get => _hasSufficientSpace;
        set { _hasSufficientSpace = value; OnPropertyChanged(); }
    }

    public bool CreateDesktopShortcut
    {
        get => _createDesktopShortcut;
        set { _createDesktopShortcut = value; OnPropertyChanged(); }
    }

    public bool CreateStartMenuShortcut
    {
        get => _createStartMenuShortcut;
        set { _createStartMenuShortcut = value; OnPropertyChanged(); }
    }

    public ImageSource? ArtworkImage
    {
        get => _artworkImage;
        set { _artworkImage = value; OnPropertyChanged(); }
    }

    public double ProgressRatio
    {
        get => _progressRatio;
        set { _progressRatio = value; OnPropertyChanged(); }
    }

    public int ProgressPercentage
    {
        get => _progressPercentage;
        set { _progressPercentage = value; OnPropertyChanged(); }
    }

    public string StatusHeaderUpper => CurrentState switch
    {
        UIState.Ready => "READY TO INSTALL",
        UIState.Installing => "INSTALLING",
        UIState.Completed => "INSTALLATION COMPLETE",
        UIState.Error => "INSTALLATION ERROR",
        _ => "READY TO INSTALL"
    };

    public string StatusDetailLine
    {
        get
        {
            if (IsErrorState) return !string.IsNullOrWhiteSpace(ErrorMessage) ? ErrorMessage : "Installation encountered an error.";
            if (IsReadyState) return $"0 B / {RequiredSpaceText}";
            if (IsInstallingState)
            {
                if (!string.IsNullOrWhiteSpace(SpeedAndEtaText))
                    return $"{ExtractedProgressText}    {SpeedAndEtaText}";
                return ExtractedProgressText;
            }
            if (IsCompletedState)
            {
                if (!string.IsNullOrWhiteSpace(SpeedAndEtaText))
                    return $"{RequiredSpaceText} / {RequiredSpaceText}    {SpeedAndEtaText}";
                return $"{RequiredSpaceText} / {RequiredSpaceText}";
            }
            return ExtractedProgressText;
        }
    }

    public string CurrentStageText
    {
        get => _currentStageText;
        set
        {
            _currentStageText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusHeaderUpper));
        }
    }

    public string CurrentFileText
    {
        get => _currentFileText;
        set { _currentFileText = value; OnPropertyChanged(); }
    }

    public string ExtractedProgressText
    {
        get => _extractedProgressText;
        set
        {
            _extractedProgressText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusDetailLine));
        }
    }

    public string SpeedAndEtaText
    {
        get => _speedAndEtaText;
        set
        {
            _speedAndEtaText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusDetailLine));
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set
        {
            _errorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusDetailLine));
        }
    }

    public string PrimaryButtonText => CurrentState switch
    {
        UIState.Ready => "INSTALL",
        UIState.Installing => "CANCEL",
        UIState.Completed => "LAUNCH",
        UIState.Error => "CLOSE",
        _ => "INSTALL"
    };

    public string LogFilePath => _fileLogger.LogFilePath;

    public ICommand BrowseDirectoryCommand { get; }
    public ICommand PrimaryActionCommand { get; }
    public ICommand CancelInstallCommand { get; }
    public ICommand LaunchGameCommand { get; }
    public ICommand OpenInstallDirectoryCommand { get; }
    public ICommand OpenLogsCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand MinimizeCommand { get; }

    public async Task InitializeAsync(string? packagePathOrDir)
    {
        try
        {
            string targetPath = !string.IsNullOrWhiteSpace(packagePathOrDir)
                ? packagePathOrDir
                : AppDomain.CurrentDomain.BaseDirectory;

            _manifest = await _engine.AnalyzeAsync(targetPath);
            _packageDir = File.Exists(targetPath) ? Path.GetDirectoryName(Path.GetFullPath(targetPath))! : Path.GetFullPath(targetPath);

            GameName = _manifest.Game.Name;
            Version = _manifest.Game.Version;
            Brand = !string.IsNullOrWhiteSpace(_manifest.Package.Brand) ? _manifest.Package.Brand : "NAGELREPACKS";

            long totalBytes = _manifest.Payload.UncompressedSize;
            RequiredSpaceText = SizeFormatter.FormatBytes(totalBytes);
            ExtractedProgressText = $"0 B / {RequiredSpaceText}";

            // Determine default games directory
            string gamesDir = "C:\\Games";
            try
            {
                var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed).ToList();
                var dDrive = drives.FirstOrDefault(d => d.Name.StartsWith("D", StringComparison.OrdinalIgnoreCase));
                if (dDrive != null) gamesDir = "D:\\Games";
            }
            catch { }

            string safeGameFolder = string.Join("_", _manifest.Game.Name.Split(Path.GetInvalidFileNameChars()));
            DestinationDirectory = Path.Combine(gamesDir, safeGameFolder);

            // Load Artwork (URL, local path, or fallback default)
            string? artworkRef = _manifest.Game.Artwork;
            await LoadArtworkAsync(artworkRef);

            CurrentState = UIState.Ready;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to discover or load package: {ex.Message}";
            CurrentState = UIState.Error;
        }
    }

    private async Task LoadArtworkAsync(string? artworkRef)
    {
        try
        {
            // 1. Check if provided as URL (http / https)
            if (!string.IsNullOrWhiteSpace(artworkRef) &&
                (artworkRef.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                 artworkRef.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                var imageBytes = await _httpClient.GetByteArrayAsync(artworkRef);
                using var ms = new MemoryStream(imageBytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze();

                ArtworkImage = bitmap;
                return;
            }

            // 2. Check local path provided in manifest
            if (!string.IsNullOrWhiteSpace(artworkRef))
            {
                string candidatePath = Path.IsPathRooted(artworkRef)
                    ? artworkRef
                    : Path.Combine(_packageDir, artworkRef);

                if (File.Exists(candidatePath))
                {
                    var bitmap = LoadBitmapFromFile(candidatePath);
                    if (bitmap != null)
                    {
                        ArtworkImage = bitmap;
                        return;
                    }
                }
            }

            // 3. Check for local artwork in package folder
            string[] possibleNames = { "artwork.jpg", "artwork.png", "cover.jpg", "cover.png", "banner.jpg", "banner.png" };
            foreach (var name in possibleNames)
            {
                var candidate = Path.Combine(_packageDir, name);
                if (File.Exists(candidate))
                {
                    var bitmap = LoadBitmapFromFile(candidate);
                    if (bitmap != null)
                    {
                        ArtworkImage = bitmap;
                        return;
                    }
                }
            }
        }
        catch
        {
            // Fallback gracefully on any load error
        }

        // 4. Default embedded asset
        LoadDefaultArtwork();
    }

    private void LoadDefaultArtwork()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/default_artwork.png", UriKind.Absolute);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = uri;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            ArtworkImage = bitmap;
        }
        catch
        {
            try
            {
                var uri = new Uri("pack://application:,,,/Assets/default_artwork.jpg", UriKind.Absolute);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = uri;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                ArtworkImage = bitmap;
            }
            catch
            {
                // Resource load fallback
            }
        }
    }

    private static BitmapImage? LoadBitmapFromFile(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private void UpdateDiskSpace()
    {
        if (_manifest == null || string.IsNullOrWhiteSpace(DestinationDirectory)) return;

        try
        {
            var space = DiskSpaceChecker.CheckSpace(DestinationDirectory, _manifest.Payload.UncompressedSize);
            AvailableSpaceText = SizeFormatter.FormatBytes(space.AvailableFreeSpaceBytes);
            HasSufficientSpace = space.HasSufficientSpace;
        }
        catch
        {
            AvailableSpaceText = "Unknown";
            HasSufficientSpace = true;
        }
    }

    private void BrowseDirectory()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Installation Directory",
            InitialDirectory = DestinationDirectory
        };

        if (dialog.ShowDialog() == true)
        {
            string safeGameFolder = _manifest != null
                ? string.Join("_", _manifest.Game.Name.Split(Path.GetInvalidFileNameChars()))
                : "Game";

            string chosen = dialog.FolderName;
            if (!Path.GetFileName(chosen).Equals(safeGameFolder, StringComparison.OrdinalIgnoreCase))
            {
                chosen = Path.Combine(chosen, safeGameFolder);
            }

            DestinationDirectory = chosen;
        }
    }

    private bool CanExecutePrimaryAction()
    {
        return CurrentState switch
        {
            UIState.Ready => HasSufficientSpace && !string.IsNullOrWhiteSpace(DestinationDirectory),
            UIState.Installing => true,
            UIState.Completed => true,
            UIState.Error => true,
            _ => false
        };
    }

    private async void ExecutePrimaryAction()
    {
        switch (CurrentState)
        {
            case UIState.Ready:
                await StartInstallAsync();
                break;
            case UIState.Installing:
                CancelInstall();
                break;
            case UIState.Completed:
                LaunchGame();
                break;
            case UIState.Error:
                CloseApplication();
                break;
        }
    }

    private async Task StartInstallAsync()
    {
        if (_manifest == null) return;

        CurrentState = UIState.Installing;
        _cts = new CancellationTokenSource();
        var installStopwatch = Stopwatch.StartNew();
        long totalPayloadBytes = _manifest.Payload.UncompressedSize;

        var options = new InstallOptions
        {
            PackagePathOrDirectory = _packageDir,
            DestinationDirectory = DestinationDirectory,
            CreateDesktopShortcut = CreateDesktopShortcut,
            CreateStartMenuShortcut = CreateStartMenuShortcut,
            SkipIntegrityCheck = false
        };

        var progress = new Progress<InstallProgress>(p =>
        {
            ProgressRatio = p.ProgressRatio;
            ProgressPercentage = (int)(p.ProgressRatio * 100);

            CurrentStageText = p.Stage switch
            {
                InstallStage.CheckingPackage => "VERIFYING CHUNKS...",
                InstallStage.CheckingDiskSpace => "CHECKING DISK SPACE...",
                InstallStage.CreatingTemporaryEnvironment => "PREPARING STAGING...",
                InstallStage.Extracting => "INSTALLING",
                InstallStage.Verifying => "VERIFYING INTEGRITY...",
                InstallStage.ExecutingActions => "RUNNING POST-INSTALL...",
                InstallStage.CreatingShortcuts => "CREATING SHORTCUTS...",
                InstallStage.Finalizing => "FINALIZING...",
                _ => p.Stage.ToString().ToUpperInvariant()
            };

            CurrentFileText = !string.IsNullOrWhiteSpace(p.CurrentFile)
                ? Path.GetFileName(p.CurrentFile)
                : string.Empty;

            ExtractedProgressText = $"{SizeFormatter.FormatBytes(p.ProcessedBytes)} / {SizeFormatter.FormatBytes(totalPayloadBytes)}";

            string speedStr = p.SpeedBytesPerSecond > 0 ? $"{SizeFormatter.FormatBytes((long)p.SpeedBytesPerSecond)}/s" : "";
            string remainingStr = p.EstimatedRemaining.HasValue ? $"~{FormatEstimatedTime(p.EstimatedRemaining.Value)} remaining" : "";

            if (!string.IsNullOrEmpty(speedStr) && !string.IsNullOrEmpty(remainingStr))
            {
                SpeedAndEtaText = $"{speedStr}  •  {remainingStr}";
            }
            else if (!string.IsNullOrEmpty(speedStr))
            {
                SpeedAndEtaText = speedStr;
            }
            else
            {
                SpeedAndEtaText = string.Empty;
            }

            OnPropertyChanged(nameof(StatusDetailLine));
        });

        var result = await _engine.InstallAsync(options, progress, _cts.Token);

        if (result.Success)
        {
            _executablePath = result.ExecutablePath;
            CurrentStageText = "INSTALLATION COMPLETE";
            CurrentFileText = string.Empty;
            ExtractedProgressText = $"{SizeFormatter.FormatBytes(totalPayloadBytes)} / {SizeFormatter.FormatBytes(totalPayloadBytes)}";
            SpeedAndEtaText = $"Completed in {installStopwatch.Elapsed:mm\\:ss}";
            ProgressPercentage = 100;
            CurrentState = UIState.Completed;
        }
        else
        {
            ErrorMessage = result.ErrorMessage ?? "Installation encountered an error.";
            CurrentFileText = ErrorMessage;
            CurrentState = UIState.Error;
        }
    }

    private static string FormatEstimatedTime(TimeSpan span)
    {
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
        if (span.TotalMinutes >= 1) return $"{span.Minutes}m {span.Seconds}s";
        return $"{span.Seconds}s";
    }

    private void CancelInstall()
    {
        if (MessageBox.Show("Are you sure you want to cancel the installation?", "Cancel Installation", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _cts?.Cancel();
        }
    }

    private void LaunchGame()
    {
        if (!string.IsNullOrEmpty(_executablePath) && File.Exists(_executablePath))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _executablePath,
                    WorkingDirectory = Path.GetDirectoryName(_executablePath)
                });
                Application.Current?.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to launch game: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
            Application.Current?.Shutdown();
        }
    }

    private void OpenInstallDirectory()
    {
        if (Directory.Exists(DestinationDirectory))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = DestinationDirectory,
                UseShellExecute = true
            });
        }
    }

    private void OpenLogs()
    {
        if (File.Exists(LogFilePath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = LogFilePath,
                UseShellExecute = true
            });
        }
    }

    private void CloseApplication() => Application.Current?.Shutdown();

    private void MinimizeWindow()
    {
        if (Application.Current?.MainWindow != null)
        {
            Application.Current.MainWindow.WindowState = WindowState.Minimized;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
