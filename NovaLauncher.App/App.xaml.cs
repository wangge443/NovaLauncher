using System.Windows;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Interop;
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
    private GlobalHotkeyService? _globalHotkey;
    private ITrayIconService? _tray;
    private IStartupRegistrationService? _startupRegistration;
    private bool _backgroundStartup;
    private bool _ready;
    private bool _explicitExit;
    private bool _trayHintShown;
    private bool _hasShown;
    private Task _hideSave = Task.CompletedTask;
    public bool IsBackgroundStartup => _backgroundStartup;
    public bool IsTrayVisible => _tray?.IsVisible == true;
    public Task ReadyTask => _startupTask;
    private Task _startupTask = Task.CompletedTask;
    private bool _closing;
    private bool _closeReady;
    private bool _activationPending;
    private bool _transparencyRefreshQueued;
    private bool _transparencyHostFailed;
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
        _backgroundStartup = e.Args.Contains("--startup", StringComparer.Ordinal);
        if (Persistence is null)
        {
            try
            {
                ApplicationDataPaths.ConfigureVerificationRoot(e.Args);
                PerformanceTrace.Enabled = ApplicationDataPaths.VerificationRoot is not null && e.Args.Contains("--performance-test");
                PerformanceTrace.Mark("ManagedStartup");
                _instance = new SingleInstanceCoordinator(ApplicationDataPaths.VerificationRoot?.ToUpperInvariant());
                if (!_instance.TryAcquire())
                {
                    if (_backgroundStartup) { Shutdown(); return; }
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
            PerformanceTrace.Log = _log;
            _traceListener = new NovaTraceListener(_log);
            Trace.Listeners.Add(_traceListener);
            _exceptionLogging = new UnhandledExceptionLogging(this, _log);
            _log.WriteStartup(typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown");
            if (_backgroundStartup) _log.Write(LogLevel.Information, "Startup", "App launched with --startup");
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
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
            _globalHotkey = new GlobalHotkeyService(_settingsService, _log);
            _globalHotkey.Triggered += OpenGlobalQuickLauncher;
            BackgroundStartupViewModel? backgroundStartup = null;
            if (Persistence is null)
            {
                // Verification uses an isolated key, never the user's actual Run entry.
                string key = ApplicationDataPaths.VerificationRoot is null ? RegistryStartupStore.RunKey
                    : @"Software\NovaLauncher\Verification\" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(paths.RootDirectory))) + @"\Run";
                string executable = ApplicationDataPaths.VerificationRoot is not null && Path.GetFileName(Environment.ProcessPath) == "NovaLauncher.Verification.exe"
                    ? Path.Combine(AppContext.BaseDirectory, "NovaLauncher.exe") : Environment.ProcessPath!;
                _startupRegistration = new StartupRegistrationService(new RegistryStartupStore(key), executable, _log);
                backgroundStartup = new BackgroundStartupViewModel(_settingsService, _startupRegistration, _content.Toast);
            }
            _appearance = new AppearanceService(_settingsService, new BackgroundImageStore(paths), new DesktopWallpaperSource(), _content.Toast);
            _settings = new SettingsViewModel(_settingsService, new DataDirectoryService(paths), paths, _content.Toast,
                new AppearanceViewModel(_appearance, new BackgroundFilePicker(), _content.Toast), new GlobalHotkeyViewModel(_globalHotkey), backgroundStartup);
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
        if (_ready) ShowMainWindow();
        else _activationPending = true;
    }

    private void ShowMainWindow()
    {
        if (_closing) return;
        bool hidden = !MainWindow.IsVisible;
        WindowActivationService.Activate(MainWindow);
        _hasShown = true;
        if (hidden) _settings?.BackgroundStartup?.Refresh();
        if (hidden) _log?.Write(LogLevel.Information, "Tray", "MainWindow restored from tray");
    }

    private void OnTrayAction(TrayAction action)
    {
        if (_closing) return;
        if (action == TrayAction.Exit) { RequestExplicitExit(); return; }
        if (action == TrayAction.QuickLauncher) { OpenGlobalQuickLauncher(); return; }
        ShowMainWindow();
        if (action == TrayAction.Settings && MainWindow.DataContext is MainWindowViewModel shell) shell.Sidebar.OpenSettings();
    }

    public void RequestExplicitExit()
    {
        if (_closing) return;
        _explicitExit = true;
        _log?.Write(LogLevel.Information, "Application", "Explicit exit");
        MainWindow.Close();
    }

    private void OpenGlobalQuickLauncher()
    {
        if (_closing || MainWindow is not MainWindow window) return;
        var timer = Stopwatch.StartNew();
        ShowMainWindow();
        double visible = timer.Elapsed.TotalMilliseconds;
        window.OpenQuickLauncher();
        double opened = timer.Elapsed.TotalMilliseconds;
        void RecordFocus()
        {
            bool focused = System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox { Name: "QueryBox" };
            _log?.Write(LogLevel.Information, "GlobalHotkey",
                FormattableString.Invariant($"Activation visibleMs={visible:F2} overlayMs={opened:F2} focusedMs={timer.Elapsed.TotalMilliseconds:F2} focused={focused}"));
        }
        if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox { Name: "QueryBox" }) RecordFocus();
        else Dispatcher.BeginInvoke(RecordFocus, System.Windows.Threading.DispatcherPriority.Input);
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
                long background = PerformanceTrace.Start();
                await _appearance!.InitializeAsync();
                ((MainWindow)MainWindow).ConfigureTransparentSurface(_appearance.IsTransparent);
                _appearance.PropertyChanged += OnAppearanceChanged;
                PerformanceTrace.End("BackgroundInitialize", background);
                ((MainWindowViewModel)MainWindow.DataContext).ApplyStartupPage(_settingsService.Current.StartupPage);
                MainWindow.SourceInitialized += (_, _) => WindowPlacementService.Restore(MainWindow, _settingsService.Current);
                PerformanceTrace.Mark("HomeDataReady");
                long layout = PerformanceTrace.Start();
                try
                {
                    if (_backgroundStartup) new WindowInteropHelper(MainWindow).EnsureHandle();
                    else { MainWindow.Show(); _hasShown = true; }
                }
                catch (Exception ex) when (_appearance.IsTransparent)
                {
                    _log?.Write(LogLevel.Error, "Appearance", "Transparent startup failed; using default window", ex);
                    TransparencySupport.DisableForSession();
                    await _appearance.FallBackFromTransparencyAsync();
                    var failed = MainWindow;
                    MainWindow = new MainWindow { DataContext = failed.DataContext };
                    MainWindow.Closing += OnWindowClosing;
                    failed.Closing -= OnWindowClosing;
                    failed.Close();
                    MainWindow.SourceInitialized += (_, _) => WindowPlacementService.Restore(MainWindow, _settingsService.Current);
                    if (_backgroundStartup) new WindowInteropHelper(MainWindow).EnsureHandle();
                    else { MainWindow.Show(); _hasShown = true; }
                    _content.Toast.Show("透明背景无法启用，已恢复默认背景");
                }
                if (!_backgroundStartup) PerformanceTrace.Mark("MainWindowShown");
                // A dedicated message HWND keeps registrations stable across Glass host swaps.
                if (Persistence is null)
                {
                    var startupState = _backgroundStartup ? _startupRegistration!.Read() : _startupRegistration!.RepairMovedPath();
                    if (startupState.Error is not null) _content.Toast.Show(startupState.Error);
                    _tray = new TrayIconService(_log);
                    _tray.Requested += action => Dispatcher.BeginInvoke(new Action(() => OnTrayAction(action)));
                    try { _tray.Initialize(); }
                    catch (Exception ex)
                    {
                        _log?.Write(LogLevel.Error, "Tray", "Tray initialization failed; keeping window accessible", ex);
                        _tray.Dispose(); _tray = null;
                        MainWindow.Show(); _hasShown = true;
                        _content.Toast.Show("系统托盘不可用，关闭窗口将退出 Nova。");
                    }
                    LogReady("Tray Ready");
                    _globalHotkey!.Initialize(new HotkeyRegistrar());
                    LogReady("Global Hotkey Ready");
                }
                if (!_backgroundStartup) MainWindow.UpdateLayout();
                PerformanceTrace.End("HomeFirstLayout", layout);
                PerformanceTrace.Mark("FirstLayoutCompleted");
                if (PerformanceTrace.Enabled && !_backgroundStartup) _ = PerformanceSession.RunAsync(MainWindow, _content);
                if (!loaded) _content.Toast.Show("设置读取失败，已使用默认设置");
            }
            _ready = true;
            if (_activationPending) { _activationPending = false; ShowMainWindow(); }
            LogReady(_backgroundStartup ? "Background Ready" : "Manual Ready");
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

    private void LogReady(string phase)
    {
        using var process = Process.GetCurrentProcess();
        _log?.Write(LogLevel.Information, "Startup", FormattableString.Invariant($"{phase} processMs={(DateTime.Now - process.StartTime).TotalMilliseconds:F2} managedBytes={GC.GetTotalMemory(false)}"));
    }

    private async Task HideToTrayAsync()
    {
        _content!.QuickLauncher.Close();
        _settings?.Hotkey?.CancelCapture();
        var placement = WindowPlacementService.Capture(_settingsService!.Current, MainWindow);
        MainWindow.Hide();
        _log?.Write(LogLevel.Information, "Tray", "MainWindow hidden to tray");
        string hint = Path.Combine(ApplicationDataPaths.Default.RootDirectory, "tray-hint-shown");
        if (!_trayHintShown && !File.Exists(hint))
        {
            _trayHintShown = true;
            _tray!.ShowBackgroundHint(_globalHotkey?.IsRegistered == true
                ? $"按 {_globalHotkey.Hotkey} 可快速启动应用。右键托盘图标可退出 Nova。"
                : "双击托盘图标可打开 Nova，右键可退出 Nova。");
            try { await File.WriteAllTextAsync(hint, "1"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log?.Write(LogLevel.Warning, "Tray", "Tray hint state could not be saved", ex); }
        }
        await _settingsService.UpdateAsync(s => s with { WindowWidth = placement.WindowWidth, WindowHeight = placement.WindowHeight, WindowLeft = placement.WindowLeft, WindowTop = placement.WindowTop });
    }

    private async void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_closeReady || _content is null) return;
        e.Cancel = true;
        if (_closing) return;
        if (!_explicitExit && _ready && _settingsService?.Current.CloseToTray == true && _tray?.IsVisible == true)
        {
            _hideSave = HideToTrayAsync();
            return;
        }
        _closing = true;
        if (_globalHotkey is not null) _globalHotkey.SuppressActivation = true;
        MainWindow.IsEnabled = false;
        try
        {
            await _startupTask; await _hideSave; await _content.FlushAsync(); if (_profile is not null) await _profile.FlushAsync();
            if (_settings is not null) await _settings.FlushAsync();
            if (_hasShown && _settingsService is not null && !await _settingsService.UpdateAsync(current => WindowPlacementService.Capture(current, MainWindow)))
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
            _globalHotkey?.Dispose();
            _tray?.Dispose();
            _instance?.Dispose();
            _log?.Write(LogLevel.Information, "Application", "Shutdown");
            if (_log is not null) await _log.StopAsync();
            // Flush may complete synchronously; defer Close until the current Closing event has returned.
            _ = Dispatcher.BeginInvoke(new Action(() => { _closeReady = true; MainWindow.Close(); Shutdown(_exitCode); }));
        }
    }

    private void OnAppearanceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AppearanceService.IsTransparent) || _transparencyRefreshQueued) return;
        _transparencyRefreshQueued = true;
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            _transparencyRefreshQueued = false;
            if (_closing || _appearance is null || MainWindow is not MainWindow old ||
                old.AllowsTransparency == _appearance.IsTransparent || _transparencyHostFailed) return;
            MainWindow? next = null;
            try
            {
                var state = old.WindowState;
                bool visible = old.IsVisible;
                next = TransparencyWindowHost.CreateReplacement(old, _appearance.IsTransparent);
                if (!_hasShown) { next.Width = old.Width; next.Height = old.Height; }
                if (visible) next.Show(); else new WindowInteropHelper(next).EnsureHandle();
                next.WindowState = state;
                MainWindow = next;
                next.Closing += OnWindowClosing;
                old.Closing -= OnWindowClosing;
                old.Close();
                if (visible && state != WindowState.Minimized) next.Activate();
            }
            catch (Exception ex)
            {
                _log?.Write(LogLevel.Error, "Appearance", "Transparent host unavailable; restoring default", ex);
                _transparencyHostFailed = true;
                TransparencySupport.DisableForSession();
                if (!ReferenceEquals(MainWindow, old)) MainWindow = old;
                if (next is not null) { next.Closing -= OnWindowClosing; next.Close(); }
                ((MainWindow)old).UseOpaqueSurface();
                await _appearance.FallBackFromTransparencyAsync();
                _content?.Toast.Show("透明背景无法启用，已恢复默认背景");
            }
        }));
    }

    private void OnSystemPreferencesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_appearance?.IsTransparent == true && !TransparencySupport.IsSupported())
            Dispatcher.BeginInvoke(new Action(async () => await _appearance.FallBackFromTransparencyAsync()));
        if (e.PropertyName is nameof(SystemParameters.ClientAreaAnimation) or nameof(SystemParameters.HighContrast))
        {
            Dispatcher.Invoke(() => ShellPreferences.Apply(Resources));
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _globalHotkey?.Dispose();
        _tray?.Dispose();
        if (_appearance is not null) _appearance.PropertyChanged -= OnAppearanceChanged;
        _content?.Dispose();
        SystemParameters.StaticPropertyChanged -= OnSystemPreferencesChanged;
        _instance?.Dispose();
        _exceptionLogging?.Dispose();
        if (_traceListener is not null) { Trace.Listeners.Remove(_traceListener); _traceListener.Dispose(); }
        if (_exitCode != 0) e.ApplicationExitCode = _exitCode;
        base.OnExit(e);
    }
}
