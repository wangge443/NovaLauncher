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
    private IAppSettingsService? _settingsService;
    private SettingsViewModel? _settings;
    private AppearanceService? _appearance;
    public Task ReadyTask => _startupTask;
    private Task _startupTask = Task.CompletedTask;
    private bool _closing;
    private bool _closeReady;

    public App() { }
    public App(IApplicationPersistenceService persistence, ICategoryService? categories = null, IUserProfileService? profile = null, IAppSettingsService? settings = null)
    {
        Persistence = persistence;
        Categories = categories;
        Profiles = profile;
        _settingsService = settings;
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
        var management = new ApplicationManagementService(new ApplicationRepository(new LauncherDbContextFactory(paths), paths),
            new ApplicationIconCache(paths, icons), new CustomIconStore(paths));
        var persistence = Persistence ?? new ApplicationPersistenceService(
            new ApplicationRepository(new LauncherDbContextFactory(paths), paths), new ApplicationIconCache(paths, icons));
        _content = new ContentViewModel(new ApplicationImportService(new ShortcutResolver(), icons),
            new ApplicationLaunchService(), persistence, new ToastService(),
            Categories ?? (Persistence is null ? new CategoryService(new CategoryRepository(new LauncherDbContextFactory(paths))) : null),
            new ApplicationLocationService(), Persistence is null ? management : null, new ApplicationIconPicker());
        _profile = new UserProfileViewModel(Profiles ?? (Persistence is null
            ? new UserProfileService(new UserProfileRepository(new LauncherDbContextFactory(paths)), paths) : null), new AvatarFilePicker(), _content.Toast);
        _settingsService ??= Persistence is null ? new AppSettingsService(new AppSettingsRepository(new LauncherDbContextFactory(paths))) : null;
        if (_settingsService is not null)
        {
            _appearance = new AppearanceService(_settingsService, new BackgroundImageStore(paths), new DesktopWallpaperSource(), _content.Toast);
            _settings = new SettingsViewModel(_settingsService, new DataDirectoryService(paths), paths, _content.Toast,
                new AppearanceViewModel(_appearance, new BackgroundFilePicker(), _content.Toast));
        }
        MainWindow = new MainWindow
        {
            DataContext = new MainWindowViewModel(_content, _profile, _settings)
        };
        MainWindow.Closing += OnWindowClosing;
        if (_settingsService is null) MainWindow.Show();
        _startupTask = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await _content!.InitializeAsync();
        await _profile!.InitializeAsync();
        if (_settingsService is not null)
        {
            bool loaded = await _settingsService.LoadAsync();
            _settings!.Refresh();
            await _appearance!.InitializeAsync();
            ((MainWindowViewModel)MainWindow.DataContext).ApplyStartupPage(_settingsService.Current.StartupPage);
            MainWindow.SourceInitialized += (_, _) => WindowPlacementService.Restore(MainWindow, _settingsService.Current);
            MainWindow.Show();
            if (!loaded) _content.Toast.Show("设置读取失败，已使用默认设置");
        }
    }

    private async void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_closeReady || _content is null) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        MainWindow.IsEnabled = false;
        try
        {
            await _startupTask; await _content.FlushAsync(); if (_profile is not null) await _profile.FlushAsync();
            if (_settings is not null) await _settings.FlushAsync();
            if (_settingsService is not null && !await _settingsService.UpdateAsync(current => WindowPlacementService.Capture(current, MainWindow)))
            {
                _content.Toast.Show("设置保存失败");
                // Keep the failure visible briefly while still honoring the user's close request.
                await Task.Delay(1200);
            }
        }
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
