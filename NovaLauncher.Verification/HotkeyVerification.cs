using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NovaLauncher.App.Models;
using NovaLauncher.App.Persistence;
using NovaLauncher.App.Services;
using NovaLauncher.App.ViewModels;
using NovaLauncher.App.Views;

namespace NovaLauncher.Verification;

internal static partial class Program
{
    private static async Task<int> SetHotkeyConfigAsync(string[] args)
    {
        ApplicationDataPaths.ConfigureVerificationRoot(["--verification-data-root=" + Path.GetFullPath(args[1])]);
        var settings = new AppSettingsService(new AppSettingsRepository(new LauncherDbContextFactory(ApplicationDataPaths.Default)));
        if (!await settings.LoadAsync()) return 1;
        return await settings.UpdateAsync(s => s with { CloseToTray = false, GlobalHotkeyEnabled = bool.Parse(args[2]), GlobalHotkeyModifiers = int.Parse(args[3]), GlobalHotkeyKey = int.Parse(args[4]) }) ? 0 : 1;
    }
    private static async Task<int> VerifyRealHotkeyUpgradeAsync(string output, bool tray = false)
    {
        string root = Path.GetFullPath(output); Directory.CreateDirectory(root);
        string original = ApplicationDataPaths.Default.DatabasePath;
        if (!File.Exists(original)) throw new FileNotFoundException("No real database available for upgrade verification.");
        string backup = Path.Combine(root, "real-before-" + (tray ? "8.6-" : "8.5-") + DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ") + ".db");
        // SQLite online backup includes WAL state. Production is opened read-only and never migrated here.
        using (var source = OpenDatabase(original, true))
        using (var target = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + backup))
        { target.Open(); source.BackupDatabase(target); }
        Check(File.Exists(backup), "Real nova.db backed up before any upgrade verification");
        var paths = new ApplicationDataPaths(Path.Combine(root, "isolated-real-copy"));
        Directory.CreateDirectory(Path.GetDirectoryName(paths.DatabasePath)!); File.Copy(backup, paths.DatabasePath);
        string[] tables = ["Applications", "Categories", "UserProfiles", "AppSettings"];
        var snapshots = new Dictionary<string, (string Columns, string Rows)>();
        using (var before = OpenDatabase(backup, true))
        {
            foreach (string table in tables)
            {
                using var command = before.CreateCommand(); command.CommandText = $"PRAGMA table_info(\"{table}\")";
                using var reader = command.ExecuteReader(); var columns = new List<string>();
                while (reader.Read()) columns.Add('"' + reader.GetString(1).Replace("\"", "\"\"") + '"');
                reader.Close(); string selection = string.Join(",", columns);
                snapshots.Add(table, (selection, Query(before, $"SELECT {selection} FROM \"{table}\" ORDER BY Id")));
            }
        }
        var factory = new LauncherDbContextFactory(paths);
        await new DatabaseInitializationService(factory, paths).InitializeAsync(default);
        using (var after = OpenDatabase(paths.DatabasePath, true))
            foreach (var pair in snapshots) Check(Query(after, $"SELECT {pair.Value.Columns} FROM \"{pair.Key}\" ORDER BY Id") == pair.Value.Rows, "Real backup upgrade preserves every existing field in " + pair.Key);
        using (var untouched = OpenDatabase(original, true))
            foreach (var pair in snapshots) Check(Query(untouched, $"SELECT {pair.Value.Columns} FROM \"{pair.Key}\" ORDER BY Id") == pair.Value.Rows, "Production remains unchanged: " + pair.Key);
        var settings = await new AppSettingsRepository(factory).GetOrCreateAsync();
        Check(tray ? settings.CloseToTray : settings.GlobalHotkeyEnabled && settings.GlobalHotkeyModifiers == 3 && settings.GlobalHotkeyKey == 32, "Real old-user copy receives correct defaults");
        Check(BackupCount(paths) == 1, "Normal migration pipeline also creates its own pre-upgrade backup");
        Console.WriteLine($"PASS: {_checks} real-backup upgrade assertions; original database not upgraded");
        return 0;
    }
    private sealed class FakeHotkeyRegistrar : IHotkeyRegistrar
    {
        public Dictionary<int, GlobalHotkey> Active { get; } = [];
        public GlobalHotkey? Conflict { get; set; }
        public event Action<int>? Pressed;
        public int Releases { get; private set; }
        public bool Register(int id, GlobalHotkey hotkey, out int error)
        {
            error = hotkey == Conflict ? 1409 : 0;
            if (error != 0) return false;
            Active.Add(id, hotkey); return true;
        }
        public void Fire(int id) => Pressed?.Invoke(id);
        public void Unregister(int id) { Active.Remove(id); Releases++; }
        public void Dispose() => Active.Clear();
    }
    private sealed class HotkeySettingsStub : IAppSettingsService
    {
        public AppSettings Current { get; private set; } = new();
        public bool Fail { get; set; }
        public Func<Task>? BeforeSave { get; set; }
        public Task<bool> LoadAsync(CancellationToken token = default) => Task.FromResult(true);
        public async Task<bool> UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken token = default)
        {
            if (BeforeSave is not null) await BeforeSave();
            if (Fail) return false;
            Current = update(Current); return true;
        }
    }
    private static async Task RunHotkeyTestsAsync()
    {
        Check(GlobalHotkey.Default.ToString() == "Ctrl + Alt + Space", "Default shortcut uses stable friendly formatting");
        foreach (var key in new[] { new GlobalHotkey(0, 65), new GlobalHotkey(0, 32), new GlobalHotkey(0, 13),
            new GlobalHotkey(0, 112), new GlobalHotkey(1, 115), new GlobalHotkey(3, 46), new GlobalHotkey(8, 76),
            new GlobalHotkey(8, 68), new GlobalHotkey(16, 65), new GlobalHotkey(2, 27), new GlobalHotkey(2, 123), new GlobalHotkey(2, 999) })
            Check(key.ValidationError is not null, "Reject reserved or invalid shortcut " + key);
        foreach (var key in new[] { GlobalHotkey.Default, new GlobalHotkey(2, 32), new GlobalHotkey(6, 75), new GlobalHotkey(5, 81) })
            Check(key.ValidationError is null, "Accept valid shortcut " + key);
        var settings = new HotkeySettingsStub();
        var registrar = new FakeHotkeyRegistrar();
        using var service = new GlobalHotkeyService(settings);
        service.Initialize(registrar);
        Check(service.IsRegistered && registrar.Active.Count == 1, "Default enabled registration succeeds");
        int fired = 0; service.Triggered += () => fired++;
        registrar.Fire(1); registrar.Fire(1); registrar.Fire(999);
        Check(fired == 2, "Repeated valid messages route one event each; unknown messages ignored");
        var next = new GlobalHotkey(6, 32);
        registrar.Conflict = next;
        Check(!await service.ConfigureAsync(true, next) && registrar.Active[1] == GlobalHotkey.Default && service.IsRegistered,
            "Conflict retains the old registered shortcut");
        Check(service.Status.Contains("占用") && service.Hotkey == GlobalHotkey.Default, "Conflict reports error without saving candidate");
        registrar.Conflict = null; settings.Fail = true;
        Check(!await service.ConfigureAsync(true, next) && registrar.Active.Count == 1 && registrar.Active[1] == GlobalHotkey.Default,
            "Persistence failure releases candidate and retains old registration");
        settings.Fail = false;
        settings.BeforeSave = () => { Check(registrar.Active.Count == 2, "Old and candidate reservations are held until commit"); return Task.CompletedTask; };
        Check(await service.ConfigureAsync(true, next), "Successful candidate persists");
        settings.BeforeSave = null;
        Check(registrar.Active.Count == 1 && registrar.Active[2] == next && service.Hotkey == next, "Registration switches only after successful save");
        registrar.Fire(1); Check(fired == 2, "Queued old shortcut message is ignored");
        registrar.Fire(2); Check(fired == 3, "New shortcut immediately triggers");
        var vm = new GlobalHotkeyViewModel(service);
        vm.BeginCaptureCommand.Execute(null); registrar.Fire(2);
        Check(vm.IsCapturing && fired == 3, "Capture mode suppresses existing global shortcut activation");
        await vm.CaptureAsync(new(0, 65)); Check(vm.IsCapturing && vm.Error is not null, "Invalid capture stays open with useful error");
        vm.CancelCapture(); Check(!vm.IsCapturing && service.Hotkey == next, "Cancel preserves shortcut");
        Check(await service.ConfigureAsync(false, next) && !service.IsRegistered && registrar.Active.Count == 0, "Disable unregisters");
        registrar.Conflict = next;
        Check(!await service.ConfigureAsync(true, next) && !service.Enabled && !service.IsRegistered, "Failed enable remains off");
        registrar.Conflict = null;
        Check(await service.ConfigureAsync(true, next) && service.IsRegistered, "Re-enable registers immediately");
        var invalid = AppSettingsService.Validate(new() { GlobalHotkeyKey = -1, GlobalHotkeyModifiers = 1024, BackgroundOverlayOpacity = .4, StartupPage = StartupPage.Recent });
        Check(invalid.GlobalHotkeyKey == 32 && invalid.GlobalHotkeyModifiers == 3 && invalid.BackgroundOverlayOpacity == .4 && invalid.StartupPage == StartupPage.Recent,
            "Invalid settings fallback preserves unrelated settings");
        service.Dispose(); Check(registrar.Active.Count == 0, "Dispose releases hotkey");
        using var startupConflict = new GlobalHotkeyService(settings);
        startupConflict.Initialize(new FakeHotkeyRegistrar { Conflict = next });
        Check(startupConflict.Enabled && !startupConflict.IsRegistered && startupConflict.Status.Contains("占用"), "Startup conflict is unavailable without changing saved preference");
        await VerifyHotkeyMigrationAsync();
    }

    private static async Task VerifyHotkeyMigrationAsync()
    {
        var paths = new ApplicationDataPaths(Path.Combine(_fixtures, "hotkey-upgrade"));
        Directory.CreateDirectory(Path.GetDirectoryName(paths.DatabasePath)!);
        var factory = new LauncherDbContextFactory(paths);
        using (var db = factory.CreateDbContext())
        {
            string previous = db.Database.GetMigrations().TakeWhile(x => !x.EndsWith("AddGlobalHotkeySettings")).Last();
            await db.GetService<IMigrator>().MigrateAsync(previous);
            await db.Database.ExecuteSqlRawAsync("INSERT INTO AppSettings (Id,StartupPage,RememberWindowSize,RememberWindowPosition,BackgroundMode,ThemeMode,UpdatedAt,WindowWidth,WindowHeight,WindowLeft,WindowTop) VALUES (1,'Recent',0,0,'Transparent','Light','2026-10-22T00:00:00+00:00',1000,700,100,150)");
        }
        await new DatabaseInitializationService(factory, paths).InitializeAsync(default);
        Check(BackupCount(paths) == 1, "Old database backed up before minimal migration");
        var repository = new AppSettingsRepository(factory);
        var before = await repository.GetOrCreateAsync();
        Check(before.GlobalHotkeyEnabled && before.GlobalHotkeyModifiers == 3 && before.GlobalHotkeyKey == 32, "Old user receives enabled Ctrl+Alt+Space defaults");
        Check(before.StartupPage == StartupPage.Recent && before.BackgroundMode == NovaLauncher.App.Models.BackgroundMode.Transparent && before.WindowWidth == 1000 && before.WindowLeft == 100,
            "Upgrade preserves startup, Glass and geometry settings");
        var settings = new AppSettingsService(repository); await settings.LoadAsync();
        Check(await settings.UpdateAsync(s => s with { GlobalHotkeyEnabled = false, GlobalHotkeyModifiers = 6, GlobalHotkeyKey = 75 }), "Hotkey fields persist to SQLite");
        var reloaded = new AppSettingsService(new AppSettingsRepository(factory)); await reloaded.LoadAsync();
        Check(!reloaded.Current.GlobalHotkeyEnabled && reloaded.Current.GlobalHotkeyModifiers == 6 && reloaded.Current.GlobalHotkeyKey == 75, "New service graph reloads exact hotkey configuration");
        await repository.SaveAsync(before, before with { WindowTop = 200 });
        Check((await repository.GetOrCreateAsync()).GlobalHotkeyKey == 75, "Stale geometry update cannot reset new shortcut");
        using var current = factory.CreateDbContext();
        Check(!current.Database.HasPendingModelChanges(), "Migration matches current model");
        await current.Database.ExecuteSqlRawAsync("UPDATE AppSettings SET GlobalHotkeyModifiers = 99, GlobalHotkeyKey = 999");
        await reloaded.LoadAsync();
        Check(reloaded.Current.GlobalHotkeyKey == 32 && reloaded.Current.GlobalHotkeyModifiers == 3 && reloaded.Current.StartupPage == StartupPage.Recent,
            "Corrupted persisted key safely falls back without losing other settings");
        await current.Database.ExecuteSqlRawAsync("UPDATE AppSettings SET GlobalHotkeyEnabled='broken', GlobalHotkeyModifiers=9999999999999, GlobalHotkeyKey='broken'");
        Check(await reloaded.LoadAsync() && reloaded.Current.GlobalHotkeyEnabled && reloaded.Current.GlobalHotkeyKey == 32 && reloaded.Current.WindowWidth == 1000,
            "Wrong SQLite storage types and oversized integers cannot discard unrelated settings");
        Check(await reloaded.UpdateAsync(s => s with { GlobalHotkeyModifiers = 6, GlobalHotkeyKey = 75 }) &&
            (await repository.GetOrCreateAsync()).GlobalHotkeyKey == 75, "User can replace a corrupt stored shortcut after fallback");
    }

    private static void SendGlobalHotkey(byte modifiers = 3, byte key = 0x20)
    {
        if ((modifiers & 2) != 0) LaunchNative.keybd_event(0x11, 0, 0, UIntPtr.Zero);
        if ((modifiers & 1) != 0) LaunchNative.keybd_event(0x12, 0, 0, UIntPtr.Zero);
        if ((modifiers & 4) != 0) LaunchNative.keybd_event(0x10, 0, 0, UIntPtr.Zero);
        try { ProfileKey(key); }
        finally
        {
            if ((modifiers & 4) != 0) LaunchNative.keybd_event(0x10, 0, 2, UIntPtr.Zero);
            if ((modifiers & 1) != 0) LaunchNative.keybd_event(0x12, 0, 2, UIntPtr.Zero);
            if ((modifiers & 2) != 0) LaunchNative.keybd_event(0x11, 0, 2, UIntPtr.Zero);
        }
    }

    private static int RunHotkeyLive(string root, string output)
    {
        ApplicationDataPaths.ConfigureVerificationRoot(["--verification-data-root=" + Path.GetFullPath(root)]);
        _screenshots = Path.GetFullPath(output); Directory.CreateDirectory(_screenshots);
        var app = new NovaLauncher.App.App(); app.InitializeComponent();
        app.Startup += (_, _) => app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            Window? other = null;
            try
            {
                await app.ReadyTask;
                var shell = (MainWindowViewModel)app.MainWindow.DataContext;
                await shell.Settings!.Appearance!.Background.SetModeAsync(NovaLauncher.App.Models.BackgroundMode.Default);
                await WaitForAsync(() => !app.MainWindow.AllowsTransparency);
                var vm = shell.Settings!.Hotkey!;
                Check(vm.Status == "已启用" && vm.Shortcut == "Ctrl + Alt + Space", "Real RegisterHotKey default succeeds");
                other = new Window { Title = "External foreground verification", Width = SystemParameters.PrimaryScreenWidth, Height = SystemParameters.PrimaryScreenHeight,
                    Left = 0, Top = 0, Background = System.Windows.Media.Brushes.LightSteelBlue,
                    Content = new TextBlock { Text = "EXTERNAL FOREGROUND WINDOW", FontSize = 30, Margin = new Thickness(30) } };
                other.Show();
                await WaitForAsync(() => Descendants<AllApplicationsView>(app.MainWindow).Any(x => x.IsFullyRendered));
                var firstCard = Descendants<ApplicationCardView>(app.MainWindow).First();
                async Task InvokeAsync(WindowState state)
                {
                    var window = app.MainWindow;
                    window.Topmost = false; window.WindowState = state;
                    // Let the overlay's deferred focus restoration finish before switching foreground.
                    await Task.Delay(250);
                    await FocusGlassTestWindowAsync(other); other.Topmost = false;
                    await Task.Delay(150);
                    Check(TransparencyNative.GetForegroundWindow() == new WindowInteropHelper(other).Handle, "Companion really owns foreground before global invocation");
                    SendGlobalHotkey();
                    await Task.Delay(300);
                    await WaitForAsync(() => shell.Content.QuickLauncher.IsOpen && Keyboard.FocusedElement is TextBox { Name: "QueryBox" });
                    Check(TransparencyNative.GetForegroundWindow() == new WindowInteropHelper(window).Handle, "Hotkey activates existing Nova HWND");
                }
                await InvokeAsync(WindowState.Normal);
                Check(ReferenceEquals(firstCard, Descendants<ApplicationCardView>(app.MainWindow).First()), "Global activation does not recreate All Applications cards");
                Check(shell.Content.QuickLauncher.SearchQuery == "", "First global open has empty query");
                CaptureCategoryWindow(app.MainWindow, "global-from-external.png");
                shell.Content.QuickLauncher.SearchQuery = "App"; SendGlobalHotkey(); await Task.Delay(150);
                var query = Descendants<TextBox>(app.MainWindow).Single(x => x.Name == "QueryBox");
                Check(query.IsKeyboardFocused && query.SelectedText == "App" && Descendants<QuickLauncherOverlay>(app.MainWindow).Count() == 1, "Repeated global shortcut selects existing query in one overlay");
                ProfileKey(0x1B); await WaitForAsync(() => !shell.Content.QuickLauncher.IsOpen);
                Check(shell.Content.IsAllApplications, "Esc preserves original All Applications page");
                await InvokeAsync(WindowState.Maximized);
                Check(app.MainWindow.WindowState == WindowState.Maximized, "Maximized remains maximized");
                shell.Content.QuickLauncher.Close(); await InvokeAsync(WindowState.Minimized);
                Check(app.MainWindow.WindowState == WindowState.Maximized, "Minimized maximized window restores prior state");
                app.MainWindow.WindowState = WindowState.Normal; shell.Content.QuickLauncher.Close();
                app.MainWindow.Hide(); SendGlobalHotkey(); await WaitForAsync(() => app.MainWindow.IsVisible && query.IsKeyboardFocused);
                Check(shell.Content.QuickLauncher.IsOpen, "Hidden main window is shown without replacement");
                shell.Content.QuickLauncher.Close(); shell.Sidebar.OpenSettings(); app.MainWindow.UpdateLayout();
                await InvokeAsync(WindowState.Normal); ProfileKey(0x1B); await Task.Delay(100);
                Check(shell.Content.Mode == WorkspaceMode.Settings, "Global overlay leaves Settings state intact");
                CaptureCategoryWindow(app.MainWindow, "hotkey-settings-default.png");
                foreach (var mode in new[] { WorkspaceMode.Category, WorkspaceMode.Favorites, WorkspaceMode.Recent })
                {
                    shell.Sidebar.SelectedItem = mode == WorkspaceMode.Category ? shell.Sidebar.Categories.First() : shell.Sidebar.FooterItems.Single(x => x.Mode == mode);
                    await InvokeAsync(WindowState.Normal);
                    shell.Content.QuickLauncher.Close(); Check(shell.Content.Mode == mode, "Global overlay preserves " + mode);
                }
                await shell.Settings.Appearance!.Background.SetModeAsync(NovaLauncher.App.Models.BackgroundMode.Transparent);
                await WaitForAsync(() => app.MainWindow.AllowsTransparency);
                var glassHandle = new WindowInteropHelper(app.MainWindow).Handle;
                await InvokeAsync(WindowState.Minimized);
                Check(app.MainWindow.AllowsTransparency && app.MainWindow.Opacity == 1 && new WindowInteropHelper(app.MainWindow).Handle == glassHandle, "Glass wake preserves HWND and alpha configuration");
                CaptureCategoryWindow(app.MainWindow, "global-glass-restored.png");
                shell.Content.QuickLauncher.Close(); shell.Sidebar.OpenSettings(); await Task.Delay(500);
                Check(shell.Content.Mode == WorkspaceMode.Settings && Descendants<GlobalHotkeyView>(app.MainWindow).Any(), "Settings screenshot contains actual hotkey controls after Glass host swap");
                CaptureCategoryWindow(app.MainWindow, "hotkey-settings-glass.png");
                var capture = (Button)Descendants<GlobalHotkeyView>(app.MainWindow).Single().FindName("CaptureButton");
                vm.BeginCaptureCommand.Execute(null); capture.Focus(); ProfileKey(0x1B); await Task.Delay(100);
                Check(!vm.IsCapturing && vm.Shortcut == "Ctrl + Alt + Space", "Actual Esc cancels keyboard recording without changing shortcut");
                using (var competing = new HotkeyRegistrar())
                {
                    Check(competing.Register(99, new(6, 75), out _), "Independent HWND reserves conflicting candidate");
                    vm.BeginCaptureCommand.Execute(null); await vm.CaptureAsync(new(6, 75));
                    Check(vm.Error?.Contains("占用") == true && vm.Shortcut == "Ctrl + Alt + Space", "Real conflict rejects candidate and preserves preference");
                    vm.CancelCapture();
                    await InvokeAsync(WindowState.Normal); shell.Content.QuickLauncher.Close();
                }
                vm.BeginCaptureCommand.Execute(null); capture.Focus(); SendGlobalHotkey(6);
                await WaitForAsync(() => vm.Shortcut == "Ctrl + Shift + Space");
                Check(vm.Shortcut == "Ctrl + Shift + Space", "Real shortcut change succeeds");
                other.Activate(); SendGlobalHotkey(); await Task.Delay(200);
                Check(!shell.Content.QuickLauncher.IsOpen, "Old shortcut no longer invokes Nova");
                SendGlobalHotkey(6); await WaitForAsync(() => shell.Content.QuickLauncher.IsOpen); shell.Content.QuickLauncher.Close();
                vm.Enabled = false; await vm.FlushAsync(); SendGlobalHotkey(6); await Task.Delay(150);
                Check(!shell.Content.QuickLauncher.IsOpen && vm.Status == "已关闭", "Disabled shortcut no longer invokes Nova");
                vm.Enabled = true; await vm.FlushAsync();
                vm.BeginCaptureCommand.Execute(null); await vm.CaptureAsync(GlobalHotkey.Default);
                var elapsed = new List<double>();
                for (int i = 0; i < 9; i++)
                {
                    shell.Content.QuickLauncher.Close(); other.Activate(); await Task.Delay(100);
                    var timer = Stopwatch.StartNew(); SendGlobalHotkey();
                    await WaitForAsync(() => Keyboard.FocusedElement is TextBox { Name: "QueryBox" } && shell.Content.QuickLauncher.IsOpen);
                    if (i > 0) elapsed.Add(timer.Elapsed.TotalMilliseconds);
                }
                double[] ordered = elapsed.Order().ToArray();
                Console.WriteLine("Hotkey input-to-focus median ms=" + ((ordered[3] + ordered[4]) / 2).ToString("F2"));
                shell.Content.QuickLauncher.Close();
                string probes = Path.GetFullPath(Path.Combine(root, "hotkey-launch-results")); Directory.CreateDirectory(probes);
                string? oldProbeOutput = Environment.GetEnvironmentVariable("NOVA_LAUNCH_TEST_OUTPUT");
                try
                {
                    Environment.SetEnvironmentVariable("NOVA_LAUNCH_TEST_OUTPUT", probes);
                    string executable = Path.Combine(AppContext.BaseDirectory, "NovaLauncher.LaunchProbe.exe");
                    await shell.Content.ImportAsync([executable]);
                    var probe = shell.Content.Applications.Single(x => x.Application.ExecutablePath == executable);
                    long count = probe.LaunchCount;
                    await InvokeAsync(WindowState.Normal);
                    Descendants<TextBox>(app.MainWindow).Single(x => x.Name == "QueryBox").Text = probe.DisplayName;
                    Check(shell.Content.QuickLauncher.Results.Contains(probe), "Imported launch probe is searchable in reused Quick Launcher");
                    await Task.Delay(150); ProfileKey(0x0D);
                    await WaitForAsync(() => probe.LaunchCount == count + 1 && !shell.Content.QuickLauncher.IsOpen);
                    await WaitForAsync(() => Directory.GetFiles(probes, "*.json").Length > 0);
                    Check(Directory.GetFiles(probes, "*.json").Length > 0, "Global open, query and Enter execute the original launch pipeline in a real child probe");
                }
                finally { Environment.SetEnvironmentVariable("NOVA_LAUNCH_TEST_OUTPUT", oldProbeOutput); }
                Console.WriteLine($"PASS: {_checks} real global hotkey assertions");
                other.Close(); other = null; app.RequestExplicitExit();
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); other?.Close(); app.Shutdown(1); }
        }));
        return app.Run();
    }
}
