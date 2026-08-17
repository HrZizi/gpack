using System.Windows;
using GPack.Installer.UI.ViewModels;

namespace GPack.Installer.UI;

public partial class App : Application
{
    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        string? targetPath = e.Args.Length > 0 ? e.Args[0] : null;

        var viewModel = new MainViewModel();
        var mainWindow = new MainWindow { DataContext = viewModel };
        mainWindow.Show();

        await viewModel.InitializeAsync(targetPath);
    }
}
