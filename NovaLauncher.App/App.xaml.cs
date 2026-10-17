using System.Windows;
using System.ComponentModel;
using NovaLauncher.App.Helpers;
using NovaLauncher.App.ViewModels;
using NovaLauncher.App.Views;
using NovaLauncher.App.Services;
using NovaLauncher.App.Persistence;

namespace NovaLauncher.App;

public partial class App : Application
{
    private ContentViewModel? _content;
    private UserProfileViewModel? _profile;
    private Task _startupTask = Task.CompletedTask;
    private bool _closing;
    private bool _closeReady;

    public App() { }
    public App(IApplicationPersistenceService persistence, ICategoryService? categories = null, IUserProfileService? profile = null)
    {
        Persistence = persistence;
        Categories = categories;
        Profiles = profile;
    }
    private IApplicationPersistenceService? Persistence { get; }
    private ICategoryService? Categories { get; }
    private IUserProfileService? Profiles { get; }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShellPreferences.Apply(Resources);
        SystemParameters.StaticPropertyChanged += OnSystemPreferencesChanged;

        var paths = ApplicationDataPaths.Default;
        var icons = new ApplicationIconService();
        var persistence = Persistence ?? new ApplicationPersistenceService(
            new ApplicationRepository(new LauncherDbContextFactory(paths), paths), new ApplicationIconCache(paths, icons));
        _content = new ContentViewModel(new ApplicationImportService(new ShortcutResolver(), icons),
            new ApplicationLaunchService(), persistence, new ToastService(),
            Categories ?? (Persistence is null ? new CategoryService(new CategoryRepository(new LauncherDbContextFactory(paths))) : null));
        _profile = new UserProfileViewModel(Profiles ?? (Persistence is null
            ? new UserProfileService(new UserProfileRepository(new LauncherDbContextFactory(paths)), paths) : null), new AvatarFilePicker(), _content.Toast);
        MainWindow = new MainWindow
        {
            DataContext = new MainWindowViewModel(_content, _profile)
        };
        MainWindow.Show();
        MainWindow.Closing += OnWindowClosing;
        _startupTask = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await _content!.InitializeAsync();
        await _profile!.InitializeAsync();
    }

    private async void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_closeReady || _content is null) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        MainWindow.IsEnabled = false;
        try { await _startupTask; await _content.FlushAsync(); if (_profile is not null) await _profile.FlushAsync(); }
        finally
        {
            // Flush may complete synchronously; defer Close until the current Closing event has returned.
            _ = Dispatcher.BeginInvoke(new Action(() => { _closeReady = true; MainWindow.Close(); }));
        }
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
