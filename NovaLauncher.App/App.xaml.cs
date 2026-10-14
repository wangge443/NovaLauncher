using System.Windows;
using System.ComponentModel;
using NovaLauncher.App.Helpers;
using NovaLauncher.App.ViewModels;
using NovaLauncher.App.Views;

namespace NovaLauncher.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShellPreferences.Apply(Resources);
        SystemParameters.StaticPropertyChanged += OnSystemPreferencesChanged;

        MainWindow = new MainWindow
        {
            DataContext = new MainWindowViewModel()
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
        SystemParameters.StaticPropertyChanged -= OnSystemPreferencesChanged;
        base.OnExit(e);
    }
}
