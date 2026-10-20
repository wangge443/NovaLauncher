using System.Windows;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using NovaLauncher.App.Diagnostics;
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
    private bool _activationPending;
    private int _exitCode;
    private SingleInstanceCoordinator? _instance;
    private NovaLog? _log;
    private NovaTraceListener? _traceListener;
    private UnhandledExceptionLogging? _exceptionLogging;

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
        if (Persistence is null)
        {
            try
            {
                ApplicationDataPaths.ConfigureVerificationRoot(e.Args);
                _instance = new SingleInstanceCoordinator(ApplicationDataPaths.VerificationRoot?.ToUpperInvariant());
                if (!_instance.TryAcquire())
                {
                    _startupTask = ActivateExistingAndExitAsync();
                    return; // No window, settings, profile, database or migrations in a secondary process.
                }
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                MessageBox.Show("无法初始化 Nova Launcher，请检查启动参数或当前用户权限。", "Nova Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }
        }
        StartPrimary();
    }

    private async Task ActivateExistingAndExitAsync()
    {
        if (await _instance!.ActivateExistingAsync()) { Shutdown(); return; }
        // The owner may have crashed while this process was connecting. Recover without a stale lock.
        if (_instance.TryAcquire()) { StartPrimary(); return; }
        MessageBox.Show("已有 Nova Launcher 暂时无法响应，请稍后重试。", "Nova Launcher", MessageBoxButton.OK, MessageBoxImage.Information);
        Shutdown(1);
    }

    private void StartPrimary()
    {
        var paths = ApplicationDataPaths.Default;
        if (Persistence is null)
        {
            _log = new NovaLog(Path.Combine(paths.RootDirectory, "Logs"));
            _traceListener = new NovaTraceListener(_log);
            Trace.Listeners.Add(_traceListener);
            _exceptionLogging = new UnhandledExceptionLogging(this, _log);
            _log.WriteStartup(typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown");
            _instance!.Listen(() => Dispatcher.BeginInvoke(new Action(RequestActivation)));
        }
        ShellPreferences.Apply(Resources);
        SystemParameters.StaticPropertyChanged += OnSystemPreferencesChanged;

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

    private void RequestActivation()
    {
        _log?.Write(LogLevel.Information, "SingleInstance", "Activation requested by secondary instance");
        if (_closing) return;
        if (MainWindow?.IsVisible == true) WindowActivationService.Activate(MainWindow);
        else _activationPending = true;
    }

    private async Task InitializeAsync()
    {
        try
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
            if (_activationPending) { _activationPending = false; WindowActivationService.Activate(MainWindow); }
            _log?.Write(LogLevel.Information, "Application", "Window ready");
        }
        catch (Exception ex)
        {
            _log?.Write(LogLevel.Critical, "Application", "Startup failed; shutting down", ex);
            if (_log is not null) await _log.StopAsync();
            MessageBox.Show("Nova Launcher 启动失败，现有数据未被删除。请查看日志。", "Nova Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
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
        catch (Exception ex)
        {
            _exitCode = 1;
            _log?.Write(LogLevel.Critical, "Application", "Shutdown flush failed; closing without further writes", ex);
        }
        finally
        {
            _log?.Write(LogLevel.Information, "Application", "Shutdown");
            if (_log is not null) await _log.StopAsync();
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
        _instance?.Dispose();
        _exceptionLogging?.Dispose();
        if (_traceListener is not null) { Trace.Listeners.Remove(_traceListener); _traceListener.Dispose(); }
        if (_exitCode != 0) e.ApplicationExitCode = _exitCode;
        base.OnExit(e);
    }
}
