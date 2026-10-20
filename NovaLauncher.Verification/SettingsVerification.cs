using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.EntityFrameworkCore;
using NovaLauncher.App.Models;
using NovaLauncher.App.Persistence;
using NovaLauncher.App.Services;
using NovaLauncher.App.ViewModels;
using NovaLauncher.App.Views;
namespace NovaLauncher.Verification;
internal static partial class Program
{
    private static AppSettingsService SettingsService(ApplicationDataPaths paths) => new(new AppSettingsRepository(new LauncherDbContextFactory(paths)));
    private sealed class SettingsDirectoryStub : IDataDirectoryService
    {
        public Task OpenAsync() => Task.FromException(new IOException("Simulated shell failure"));
    }
    private static async Task RunSettingsAsync(MainWindow window)
    {
        var paths = new ApplicationDataPaths(Path.Combine(_fixtures, "settings"));
        using var content = RecentContent(paths); await content.InitializeAsync();
        await content.ImportAsync([Path.Combine(Environment.SystemDirectory, "notepad.exe")]);
        var profile = new UserProfileViewModel(ProfileService(paths), new ProfilePicker(), content.Toast);
        await profile.InitializeAsync(); string before = SettingsExistingState(paths);
        var service = SettingsService(paths);
        Check(await service.LoadAsync(), "Settings initialize after migrations");
        Check(service.Current is { StartupPage: StartupPage.Home, RememberWindowSize: true, RememberWindowPosition: true, BackgroundMode: NovaLauncher.App.Models.BackgroundMode.Default, ThemeMode: NovaLauncher.App.Models.ThemeMode.Light }, "Default settings match contract");
        await SettingsService(paths).LoadAsync();
        using (var db = new LauncherDbContextFactory(paths).CreateDbContext())
        {
            Check(await db.AppSettings.CountAsync() == 1, "Repeated initialization is idempotent");
            Check(!db.Database.HasPendingModelChanges() && !(await db.Database.GetPendingMigrationsAsync()).Any(), "Migration and snapshot match");
        }
        var settings = new SettingsViewModel(service, new SettingsDirectoryStub(), paths, content.Toast);
        var shell = new MainWindowViewModel(content, profile, settings);
        window.DataContext = shell; window.Topmost = true; window.Activate(); window.UpdateLayout();
        var profileView = Descendants<ProfileView>(window).Single();
        await ClickFavoriteElementAsync(window, (Button)profileView.FindName("AvatarButton"));
        var flyout = (Popup)profileView.FindName("Flyout"); await WaitForAsync(() => flyout.IsOpen);
        var settingButton = Descendants<Button>(flyout.Child).Single(x => System.Windows.Automation.AutomationProperties.GetName(x) == "打开设置");
        settingButton.Focus(); ProfileKey(0x0D);
        await Task.Delay(150); window.UpdateLayout();
        Check(content.Mode == WorkspaceMode.Settings && !flyout.IsOpen, "Profile Flyout enters Settings workspace");
        Check(shell.Sidebar.TopSelectedItem is null && shell.Sidebar.FooterSelectedItem is null, "Settings has no sidebar active item");
        var view = Descendants<SettingsView>(window).Single();
        Check(Descendants<ScrollViewer>(view).Count(x => x.TemplatedParent is not TextBox) == 1, "Single vertical scroll viewer");
        SaveWindow(window, "settings.png");
        var toggle = (CheckBox)view.FindName("RememberSize");
        toggle.Focus(); ProfileKey(0x20); await Task.Delay(150); await settings.FlushAsync();
        Check(!settings.RememberWindowSize && !(await new AppSettingsRepository(new LauncherDbContextFactory(paths)).GetOrCreateAsync()).RememberWindowSize, "Space toggles and immediately commits");
        settings.RememberWindowSize = true; await settings.FlushAsync();
        var selector = (ComboBox)view.FindName("StartupSelector");
        selector.Focus(); ProfileKey(0x28); await Task.Delay(150); await settings.FlushAsync();
        Check(settings.StartupPage == StartupPage.Recent, "Selector keyboard works");
        selector.IsDropDownOpen = true; await Task.Delay(100); Check(Descendants<ComboBoxItem>(((Popup)selector.Template.FindName("PART_Popup", selector)).Child).Count() == 3, "Selector popup exposes three choices"); selector.IsDropDownOpen = false;
        foreach (var page in Enum.GetValues<StartupPage>())
        {
            settings.StartupPage = page; await settings.FlushAsync();
            var restored = SettingsService(paths); await restored.LoadAsync(); shell.ApplyStartupPage(restored.Current.StartupPage);
            Check(content.Mode.ToString() == page.ToString(), "Startup route restores " + page);
        }
        foreach (var item in shell.Sidebar.Items.Concat(shell.Sidebar.FooterItems))
        {
            shell.Sidebar.OpenSettings(); shell.Sidebar.SelectedItem = item;
            Check(content.Mode != WorkspaceMode.Settings, "Leave Settings via " + item.Name);
        }
        using (var connection = OpenDatabase(paths.DatabasePath))
        using (var transaction = connection.BeginTransaction(deferred: false))
        {
            bool old = settings.RememberWindowPosition;
            settings.RememberWindowPosition = !old; await settings.FlushAsync();
            Check(settings.RememberWindowPosition == old && content.Toast.Message == "设置保存失败", "SQLite lock rolls back toggle with toast");
            var oldPage = settings.StartupPage; settings.StartupPage = StartupPage.Recent; await settings.FlushAsync();
            Check(settings.StartupPage == oldPage && content.Toast.Message == "设置保存失败", "SQLite lock rolls back selector");
            transaction.Rollback();
        }
        await settings.OpenDataDirectoryCommand.ExecuteAsync(null);
        Check(content.Toast.Message == "无法打开数据目录", "Shell failure is nonfatal");
        settings.AppearanceCommand.Execute(null);
        Check(content.Toast.Message == "外观设置将在下一阶段提供", "Appearance placeholder feedback");
        var primary = new Rect(0, 0, 1920, 1040); var secondary = new Rect(-1600, 0, 1600, 900);
        var valid = new AppSettings { WindowWidth = 900, WindowHeight = 600, WindowLeft = -1400, WindowTop = 100 };
        Check(WindowPlacementService.Resolve(valid, [primary, secondary], primary) == new Rect(-1400,100,900,600), "Negative secondary monitor restores");
        Check(primary.Contains(WindowPlacementService.Resolve(valid, [primary], primary)), "Disconnected monitor falls back");
        var bad = WindowPlacementService.Resolve(valid with { WindowWidth = -100, WindowHeight = 999999, WindowLeft = double.NaN, WindowTop = double.PositiveInfinity }, [primary], primary);
        Check(primary.Contains(bad) && bad.Width >= 800, "Invalid geometry clamped");
        Check(WindowPlacementService.Resolve(valid with { RememberWindowSize = false, RememberWindowPosition = false }, [primary], primary) == new Rect(400,160,1120,720), "Disabled flags use default geometry");
        var scaled = new Rect(0, 0, 1280, 680);
        Check(scaled.Contains(WindowPlacementService.Resolve(valid, [scaled], scaled)), "Resolution and scale change stays visible");
        Check(primary.Contains(WindowPlacementService.Resolve(valid with { WindowLeft = 100, WindowTop = -550 }, [primary], primary)), "Hidden title bar rescued");
        window.Width = 960; window.Height = 650; window.Left = 80; window.Top = 70; window.UpdateLayout();
        var captured = WindowPlacementService.Capture(service.Current, window); await service.UpdateAsync(_ => captured);
        var reload = SettingsService(paths); await reload.LoadAsync();
        Check(reload.Current.WindowWidth == window.ActualWidth && reload.Current.WindowLeft == window.Left, "Bounds persist across services");
        window.WindowState = WindowState.Maximized; await Task.Delay(150);
        var maximized = WindowPlacementService.Capture(service.Current, window);
        Check(maximized.WindowWidth == captured.WindowWidth && maximized.WindowLeft == captured.WindowLeft, "Maximized saves RestoreBounds");
        window.WindowState = WindowState.Normal;
        using (var db = new LauncherDbContextFactory(paths).CreateDbContext()) await db.Database.ExecuteSqlRawAsync("UPDATE AppSettings SET StartupPage = 'invalid'");
        var invalid = SettingsService(paths); await invalid.LoadAsync();
        Check(invalid.Current.StartupPage == StartupPage.Home, "Invalid startup page falls back");
        var missing = SettingsService(new ApplicationDataPaths(Path.Combine(_fixtures, "missing")));
        Check(!await missing.LoadAsync() && missing.Current.RememberWindowSize, "Read failure defaults");
        Check(SettingsExistingState(paths) == before, "All old data and image bytes preserved");
        await WaitForAsync(() => !content.Toast.IsOpen); shell.Sidebar.OpenSettings(); await Task.Delay(150); System.Windows.Input.Keyboard.ClearFocus(); window.Width = 800; window.Height = 560; window.UpdateLayout();
        ((ColumnDefinition)window.FindName("SidebarColumn")).Width = new GridLength(280); window.UpdateLayout();
        view = Descendants<SettingsView>(window).Single(); var scroll = Descendants<ScrollViewer>(view).Single(x => x.TemplatedParent is not TextBox);
        Check(scroll.ScrollableWidth == 0 && scroll.ScrollableHeight > 0, "Minimum window scrolls vertically only");
        SaveWindow(window, "settings-small.png"); scroll.ScrollToEnd(); window.UpdateLayout(); SaveWindow(window, "settings-storage-about.png");
        string informationalVersion = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(SettingsViewModel).Assembly)!.InformationalVersion.Split('+')[0];
        Check(settings.Version == informationalVersion, "About uses assembly informational version including preview suffix");
        Check(settings.DataDirectory == paths.RootDirectory && settings.DatabasePath == paths.DatabasePath, "Paths use ApplicationDataPaths");
    }
    private static string SettingsExistingState(ApplicationDataPaths paths)
    {
        using var connection = OpenDatabase(paths.DatabasePath, true);
        string profile = Query(connection, "SELECT * FROM UserProfiles ORDER BY Id");
        var avatars = Directory.Exists(paths.ProfileDirectory) ? Directory.GetFiles(paths.ProfileDirectory).OrderBy(x => x).Select(x => Path.GetFileName(x) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x)))).ToArray() : [];
        return JsonSerializer.Serialize(new { existing = ProfileExistingState(paths), profile, avatars });
    }
}
