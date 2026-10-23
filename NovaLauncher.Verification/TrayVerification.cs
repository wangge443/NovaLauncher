using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Win32;
using NovaLauncher.App.Models;
using NovaLauncher.App.Persistence;
using NovaLauncher.App.Services;
using NovaLauncher.App.ViewModels;
using NovaLauncher.App.Views;

namespace NovaLauncher.Verification;

internal static partial class Program
{
    private sealed class StartupStoreStub : IStartupRegistrationStore
    {
        public string? Value { get; set; }
        public bool Fail { get; set; }
        public bool FailRead { get; set; }
        public string? Read() => FailRead ? throw new UnauthorizedAccessException() : Value;
        public void Write(string value) { if (Fail) throw new UnauthorizedAccessException(); Value = value; }
        public void Delete() { if (Fail) throw new UnauthorizedAccessException(); Value = null; }
    }
    private static async Task RunTrayTestsAsync()
    {
        var store = new StartupStoreStub();
        const string path = @"D:\Nova Launcher\NovaLauncher.exe";
        var service = new StartupRegistrationService(store, path);
        Check(!service.Read().Enabled, "Startup defaults off when registry is absent");
        Check(service.SetEnabled(true).Enabled, "Startup enable reflects successful registry write");
        Check(store.Value == "\"D:\\Nova Launcher\\NovaLauncher.exe\" --startup", "Run command quotes absolute path containing spaces");
        Check(!service.SetEnabled(false).Enabled && store.Value is null, "Disable removes only startup value");
        store.Value = "\"C:\\Old Nova\\NovaLauncher.exe\" --startup";
        Check(service.RepairMovedPath().Enabled && store.Value == StartupRegistrationService.CommandFor(path), "Moved portable path repairs an existing opt-in");
        store.Value = null; service.RepairMovedPath();
        Check(store.Value is null, "Removed registry entry never recreates an opt-in");
        store.Value = "unrelated command"; service.RepairMovedPath();
        Check(store.Value == "unrelated command", "Unrecognized Run value is not silently replaced");
        store.Value = null; store.Fail = true;
        Check(!service.SetEnabled(true).Enabled && service.SetEnabled(true).Error is not null, "Denied enable stays off and reports failure");
        store.Fail = false; service.SetEnabled(true); store.Fail = true;
        Check(service.SetEnabled(false).Enabled && service.SetEnabled(false).Error is not null, "Denied disable truthfully remains enabled");
        store.FailRead = true;
        Check(service.Read().Error is not null, "Registry read error is surfaced");
        store.FailRead = false; store.Fail = false; store.Value = null;
        foreach (string invalid in new[] { "NovaLauncher.exe", @"C:\Apps\other.exe", "C:\\bad\"path\\NovaLauncher.exe" })
        {
            bool rejected = false;
            try { StartupRegistrationService.CommandFor(invalid); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Invalid startup executable rejected");
        }
        var settings = new HotkeySettingsStub();
        var toast = new ToastService();
        var vm = new BackgroundStartupViewModel(settings, service, toast);
        vm.Refresh(); Check(!vm.StartWithWindows && vm.CloseToTray, "Independent defaults: startup off and close-to-tray on");
        store.Fail = true; vm.StartWithWindows = true;
        Check(!vm.StartWithWindows && toast.IsOpen && vm.Status is not null, "Settings failure restores actual switch and shows feedback");
        store.Fail = false; vm.StartWithWindows = true;
        Check(vm.StartWithWindows && store.Value is not null, "Settings startup enable succeeds");
        vm.CloseToTray = false; await vm.FlushAsync();
        Check(!vm.CloseToTray && vm.StartWithWindows, "Close-to-tray is independent of startup");
        settings.Fail = true; vm.CloseToTray = true; await vm.FlushAsync();
        Check(!vm.CloseToTray && vm.CanEdit, "Close-to-tray save failure retains old value");
        store.Value = null; vm.Refresh(); Check(!vm.StartWithWindows, "External registry removal refreshes Settings to off");
        using (var tray = new TrayIconService())
        {
            Check(!tray.IsVisible, "Tray starts uninitialized");
            tray.Initialize(); Check(tray.IsVisible, "Actual NotifyIcon initializes with branded resource");
            tray.Initialize(); Check(tray.IsVisible, "Tray initialization is idempotent");
            tray.Dispose(); Check(!tray.IsVisible, "Tray disposal hides and releases icon");
            tray.Dispose(); Check(!tray.IsVisible, "Tray disposal is idempotent");
        }
        var paths = new ApplicationDataPaths(Path.Combine(_fixtures, "tray-upgrade"));
        Directory.CreateDirectory(Path.GetDirectoryName(paths.DatabasePath)!);
        var factory = new LauncherDbContextFactory(paths);
        using (var db = factory.CreateDbContext())
        {
            string previous = db.Database.GetMigrations().Single(x => x.EndsWith("AddGlobalHotkeySettings"));
            await db.GetService<IMigrator>().MigrateAsync(previous);
            await db.Database.ExecuteSqlRawAsync("INSERT INTO AppSettings (Id,StartupPage,RememberWindowSize,RememberWindowPosition,BackgroundMode,ThemeMode,UpdatedAt,GlobalHotkeyModifiers,GlobalHotkeyKey) VALUES (1,'Recent',0,0,'Transparent','Light','2026-10-23T00:00:00+00:00',6,75)");
        }
        await new DatabaseInitializationService(factory, paths).InitializeAsync(default);
        Check(BackupCount(paths) == 1, "8.5 database backed up before CloseToTray migration");
        var repository = new AppSettingsRepository(factory);
        var original = await repository.GetOrCreateAsync();
        Check(original.CloseToTray && original.GlobalHotkeyModifiers == 6 && original.GlobalHotkeyKey == 75 && original.BackgroundMode == NovaLauncher.App.Models.BackgroundMode.Transparent && original.StartupPage == StartupPage.Recent,
            "Upgrade adds default residency without changing hotkey or background");
        await repository.SaveAsync(original, original with { CloseToTray = false });
        Check(!(await new AppSettingsRepository(factory).GetOrCreateAsync()).CloseToTray, "Residency choice persists across repository instances");
        await repository.SaveAsync(original, original with { WindowTop = 180 });
        Check(!(await repository.GetOrCreateAsync()).CloseToTray, "Stale geometry save cannot reset residency");
        using var current = factory.CreateDbContext();
        Check(!current.Database.HasPendingModelChanges(), "CloseToTray migration matches current EF model");
    }

    private static int VerifyRealStartupRegistry()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryStartupStore.RunKey, writable: true);
        string name = RegistryStartupStore.ValueName;
        bool existed = key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase);
        object? original = existed ? key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) : null;
        RegistryValueKind kind = existed ? key.GetValueKind(name) : RegistryValueKind.String;
        try
        {
            string path = Path.GetFullPath("artifacts/publish/self-contained/NovaLauncher.exe");
            Check(File.Exists(path), "Registry test uses actual published EXE");
            var service = new StartupRegistrationService(new RegistryStartupStore(), path);
            Check(service.SetEnabled(true).Enabled, "Real HKCU Run enable succeeds without elevation requirement in product");
            Check((string?)key.GetValue(name) == StartupRegistrationService.CommandFor(path), "Real Run value contains published path and --startup");
            Check(!service.SetEnabled(false).Enabled && key.GetValue(name) is null, "Real HKCU Run disable removes value");
        }
        finally
        {
            if (existed) key.SetValue(name, original!, kind); else key.DeleteValue(name, false);
        }
        Check(existed ? Equals(key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames), original) && key.GetValueKind(name) == kind : key.GetValue(name) is null,
            "Real user Run value and type restored exactly");
        Console.WriteLine($"PASS: {_checks} real registry assertions");
        return 0;
    }

    private static int RunTrayLive(string root, string output, bool startup, bool probe)
    {
        ApplicationDataPaths.ConfigureVerificationRoot(["--verification-data-root=" + Path.GetFullPath(root)]);
        _screenshots = Path.GetFullPath(output); Directory.CreateDirectory(_screenshots);
        var app = new NovaLauncher.App.App(); app.InitializeComponent();
        app.Startup += (_, _) => app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            Window? backdrop = null;
            try
            {
                await app.ReadyTask;
                var shell = (MainWindowViewModel)app.MainWindow.DataContext;
                Check(app.IsTrayVisible, "Production graph creates real tray icon");
                Check(app.IsBackgroundStartup == startup, "--startup parsing matches launch mode");
                Check(shell.Settings!.Hotkey!.Status == "已启用", "Global hotkey registered before Background Ready");
                if (startup)
                {
                    Check(!app.MainWindow.IsVisible, "--startup never shows main window");
                    Check(!shell.Content.QuickLauncher.IsOpen, "--startup does not open overlay");
                    Check(!Descendants<ApplicationCardView>(app.MainWindow).Any(), "Background Ready has zero ApplicationCard visuals");
                }
                else Check(app.MainWindow.IsVisible, "Manual launch shows complete window");
                if (probe)
                {
                    if (!startup) await WaitForAsync(() => Descendants<AllApplicationsView>(app.MainWindow).Any(x => x.IsFullyRendered));
                    using var measured = Process.GetCurrentProcess();
                    Console.WriteLine(FormattableString.Invariant($"PROBE mode={(startup ? "background" : "manual-full")} count={shell.Content.Applications.Count} processMs={(DateTime.Now - measured.StartTime).TotalMilliseconds:F2} cards={Descendants<ApplicationCardView>(app.MainWindow).Count()} managedBytes={GC.GetTotalMemory(false)}"));
                    app.RequestExplicitExit(); return;
                }
                backdrop = new Window { WindowStyle = WindowStyle.None, Left = 0, Top = 0,
                    Width = SystemParameters.PrimaryScreenWidth, Height = SystemParameters.PrimaryScreenHeight,
                    Background = System.Windows.Media.Brushes.LightSteelBlue, ShowInTaskbar = false };
                backdrop.Show();
                await shell.Settings.Appearance!.Background.SetModeAsync(NovaLauncher.App.Models.BackgroundMode.Default);
                await WaitForAsync(() => !app.MainWindow.AllowsTransparency);
                Check(!startup || !app.MainWindow.IsVisible, "Background appearance changes do not show hidden window");
                shell.Settings.BackgroundStartup!.CloseToTray = true;
                await shell.Settings.BackgroundStartup.FlushAsync();
                SendGlobalHotkey();
                await WaitForAsync(() => app.MainWindow.IsVisible && shell.Content.QuickLauncher.IsOpen && Keyboard.FocusedElement is TextBox { Name: "QueryBox" });
                Check(shell.Content.QuickLauncher.SearchQuery == "", "Hidden hotkey opens focused empty Quick Launcher");
                shell.Content.QuickLauncher.Close();
                app.MainWindow.WindowState = WindowState.Maximized;
                await Task.Delay(250);
                app.MainWindow.Close(); await WaitForAsync(() => !app.MainWindow.IsVisible);
                Check(app.IsTrayVisible, "X hides while process and tray remain alive");
                SendGlobalHotkey(); await WaitForAsync(() => app.MainWindow.IsVisible && shell.Content.QuickLauncher.IsOpen);
                Check(app.MainWindow.WindowState == WindowState.Maximized, "Tray wake preserves maximized state");
                shell.Content.QuickLauncher.Close(); app.MainWindow.WindowState = WindowState.Normal;
                shell.Sidebar.OpenSettings(); await Task.Delay(200);
                CaptureCategoryWindow(app.MainWindow, "tray-settings-default.png");
                await shell.Settings.Appearance!.Background.SetModeAsync(NovaLauncher.App.Models.BackgroundMode.Transparent);
                await WaitForAsync(() => app.MainWindow.AllowsTransparency);
                await Task.Delay(250);
                IntPtr handle = new WindowInteropHelper(app.MainWindow).Handle;
                app.MainWindow.Close(); await WaitForAsync(() => !app.MainWindow.IsVisible);
                SendGlobalHotkey(); await WaitForAsync(() => app.MainWindow.IsVisible && shell.Content.QuickLauncher.IsOpen && Keyboard.FocusedElement is TextBox { Name: "QueryBox" });
                Check(app.MainWindow.AllowsTransparency && app.MainWindow.Opacity == 1 && new WindowInteropHelper(app.MainWindow).Handle == handle,
                    "Glass hide and restore keeps original HWND and full window opacity");
                Check(shell.Content.Mode == WorkspaceMode.Settings, "Tray restore preserves Settings page");
                shell.Content.QuickLauncher.Close(); await Task.Delay(200);
                CaptureCategoryWindow(app.MainWindow, "tray-settings-glass.png");
                app.MainWindow.WindowState = WindowState.Minimized; await Task.Delay(200);
                Check(app.MainWindow.IsVisible && app.MainWindow.WindowState == WindowState.Minimized, "Minimize remains ordinary taskbar minimize");
                SendGlobalHotkey(); await WaitForAsync(() => app.MainWindow.WindowState != WindowState.Minimized);
                app.MainWindow.Close(); await WaitForAsync(() => !app.MainWindow.IsVisible);
                using var process = Process.GetCurrentProcess();
                await Task.Delay(3000); // Exclude completion of the hide animation and pending saves.
                process.Refresh();
                TimeSpan cpu = process.TotalProcessorTime;
                long memory = GC.GetTotalMemory(false);
                await Task.Delay(30000);
                process.Refresh();
                double cpuPercent = (process.TotalProcessorTime - cpu).TotalMilliseconds / 30000 / Environment.ProcessorCount * 100;
                Console.WriteLine($"Hidden 30s cpuPercent={cpuPercent:F3} managedBefore={memory} managedAfter={GC.GetTotalMemory(false)} workingSet={process.WorkingSet64}");
                Check(!app.MainWindow.IsVisible && app.IsTrayVisible, "Background residency survives idle interval");
                shell.Settings.BackgroundStartup.CloseToTray = false; await shell.Settings.BackgroundStartup.FlushAsync();
                Console.WriteLine($"PASS: {_checks} tray live assertions; close-to-tray off exits next");
                backdrop.Close(); backdrop = null;
                app.MainWindow.Close();
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); backdrop?.Close(); app.Shutdown(1); }
        }));
        return app.Run();
    }
}
