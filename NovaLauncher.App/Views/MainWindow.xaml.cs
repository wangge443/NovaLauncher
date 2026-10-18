using System.Windows;
using System.Windows.Input;
using NovaLauncher.App.ViewModels;

namespace NovaLauncher.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.K || Keyboard.Modifiers != ModifierKeys.Control || DataContext is not MainWindowViewModel vm) return;
        if (QuickOverlay.IsComposing) return;
        QuickOverlay.OpenAndFocus();
        e.Handled = true;
    }

    private void ShellLayout_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // GridSplitter retains star sizing; enforce the limit again when the window resizes.
        SidebarColumn.MaxWidth = Math.Max(SidebarColumn.MinWidth, ShellLayout.ActualWidth * 0.35);
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
