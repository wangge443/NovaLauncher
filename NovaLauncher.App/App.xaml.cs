using System.Windows;
using System.ComponentModel;
using NovaLauncher.App.Helpers;
using NovaLauncher.App.ViewModels;
using NovaLauncher.App.Views;
using NovaLauncher.App.Services;

namespace NovaLauncher.App;

public partial class App : Application
{
    private ContentViewModel? _content;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShellPreferences.Apply(Resources);
        SystemParameters.StaticPropertyChanged += OnSystemPreferencesChanged;

        _content = new ContentViewModel(new ApplicationImportService(new ShortcutResolver(), new ApplicationIconService()),
            new ApplicationLaunchService());
        MainWindow = new MainWindow
        {
            DataContext = new MainWindowViewModel(_content)
        };
        MainWindow.Show();
    }

    private void OnSystemPreferencesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemParameters.ClientAreaAnimation) or nameof(SystemParameters.HighContrast))
        {
            Dispatcher.Invoke(() => ShellPreferences.Apply(Resources));
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _content?.Dispose();
        SystemParameters.StaticPropertyChanged -= OnSystemPreferencesChanged;
        base.OnExit(e);
    }
}
