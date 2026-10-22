using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using NovaLauncher.App.ViewModels;

namespace NovaLauncher.App.Views;

public partial class MainWindow : Window
{
    private ResourceDictionary? _glassResources;
    public MainWindow()
    {
        InitializeComponent();
    }

    public void ConfigureTransparentSurface(bool enabled)
    {
        // Must run before the HWND is created. Runtime mode changes replace only the window host.
        AllowsTransparency = enabled;
        if (!enabled) return;
        Background = Brushes.Transparent;
        var root = (System.Windows.Controls.Grid)WindowFrame.Child;
        root.Children.OfType<ShellBackgroundView>().Single().IsTransparentSurface = true;
        _glassResources = new ResourceDictionary { Source = new Uri("/NovaLauncher;component/Themes/GlassMode.xaml", UriKind.Relative) };
        Resources.MergedDictionaries.Add(_glassResources);
        root.Children.OfType<System.Windows.Controls.Grid>().First().SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "GlassChromeSurface");
    }

    public void UseOpaqueSurface()
    {
        if (_glassResources is not null) Resources.MergedDictionaries.Remove(_glassResources);
        _glassResources = null;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        var root = (System.Windows.Controls.Grid)WindowFrame.Child;
        root.Children.OfType<System.Windows.Controls.Grid>().First().Background = Brushes.Transparent;
        root.Children.OfType<ShellBackgroundView>().Single().IsTransparentSurface = false;
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
