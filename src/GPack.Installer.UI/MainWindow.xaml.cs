using System.Windows;
using System.Windows.Input;

namespace GPack.Installer.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch
            {
                // Ignore if mouse was released rapidly
            }
        }
    }
}
