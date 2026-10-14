using System.Windows;

namespace NovaLauncher.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    // Window operations belong to the view; no platform controls enter the ViewModel.
    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);
}
