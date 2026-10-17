using System.IO;
using System.Text.Json;
using System.Windows;
using NovaLauncher.App.Models;
using NovaLauncher.App.Persistence;
using NovaLauncher.App.Services;
using NovaLauncher.App.ViewModels;
using Microsoft.EntityFrameworkCore;
namespace NovaLauncher.Verification;
internal static partial class Program
{
    private static int RunSettingsPreview(bool verifyShell = false)
    {
        var app = new NovaLauncher.App.App(); app.InitializeComponent();
        app.Startup += (_, _) => app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                await app.ReadyTask;
                var shell = (MainWindowViewModel)app.MainWindow.DataContext;
                if (verifyShell) await OpenSettingsDirectoryForTestAsync(shell.Settings!, ApplicationDataPaths.Default);
                shell.Sidebar.OpenSettings(); await Task.Delay(200); app.MainWindow.UpdateLayout();
                _screenshots = Path.GetFullPath("docs/screenshots/6.2"); Directory.CreateDirectory(_screenshots);
                SaveWindow(app.MainWindow, "real-settings.png");
                var view = Descendants<NovaLauncher.App.Views.SettingsView>(app.MainWindow).Single();
                var scroll = Descendants<System.Windows.Controls.ScrollViewer>(view).Single();
                scroll.ScrollToEnd(); await Task.Delay(100); SaveWindow(app.MainWindow, "real-settings-storage.png");
                app.MainWindow.Close();
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); app.Shutdown(1); }
        }));
        return app.Run();
    }
    private sealed record SettingsSnapshot(string Existing, int Step, AppSettings? Original = null, AppSettings? Expected = null);
    private static async Task<int> VerifyRealSettingsAsync()
    {
        var paths = ApplicationDataPaths.Default;
        string snapshot = Path.Combine(Path.GetTempPath(), "Nova-settings-" + Guid.NewGuid().ToString("N") + ".json");
        AppSettings? original = null;
        try
        {
            Directory.CreateDirectory(paths.BackupsDirectory);
            string backup = Path.Combine(paths.BackupsDirectory, $"nova-before-settings-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}.db");
            using (File.Create(backup)) { }
            using (var source = OpenDatabase(paths.DatabasePath, true)) using (var destination = OpenDatabase(backup)) source.BackupDatabase(destination);
            Console.WriteLine("Backup: " + backup);
            string existing = SettingsExistingState(paths);
            for (int step = 0; step < 4; step++)
            {
                var state = step == 0 ? new SettingsSnapshot(existing, step) : JsonSerializer.Deserialize<SettingsSnapshot>(File.ReadAllText(snapshot))! with { Step = step };
                File.WriteAllText(snapshot, JsonSerializer.Serialize(state));
                await RunCategoryChildAsync("--settings-real-window", snapshot);
                original = JsonSerializer.Deserialize<SettingsSnapshot>(File.ReadAllText(snapshot))!.Original;
                Check(SettingsExistingState(paths) == existing, "Real database old rows and image hashes unchanged after process " + step);
            }
            Console.WriteLine("PASS: real settings migration and four independent WPF launches");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            if (original is null && File.Exists(snapshot)) original = JsonSerializer.Deserialize<SettingsSnapshot>(File.ReadAllText(snapshot))?.Original;
            if (original is not null) await new AppSettingsRepository(new LauncherDbContextFactory(paths)).SaveAsync(original);
            File.Delete(snapshot);
        }
    }
    private static int RunRealSettingsWindow(string snapshotPath)
    {
        var app = new NovaLauncher.App.App(); app.InitializeComponent();
        app.Startup += (_, _) => app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                await app.ReadyTask;
                var paths = ApplicationDataPaths.Default;
                var state = JsonSerializer.Deserialize<SettingsSnapshot>(File.ReadAllText(snapshotPath))!;
                var shell = (MainWindowViewModel)app.MainWindow.DataContext;
                var settings = shell.Settings!;
                var service = SettingsService(paths); await service.LoadAsync();
                Check(SettingsExistingState(paths) == state.Existing, "Production migration preserves applications categories favorites stats profile avatars icons");
                using (var db = new LauncherDbContextFactory(paths).CreateDbContext())
                    Check(await db.AppSettings.CountAsync() == 1 && !(await db.Database.GetPendingMigrationsAsync()).Any(), "Real migration and singleton complete");
                state = state with { Original = state.Original ?? service.Current };
                File.WriteAllText(snapshotPath, JsonSerializer.Serialize(state));
                if (state.Step > 0)
                {
                    Check(shell.Content.Mode.ToString() == state.Expected!.StartupPage.ToString(), "Production startup route " + state.Expected.StartupPage);
                    if (state.Step == 1)
                    {
                        Check(Math.Abs(app.MainWindow.Width - state.Expected.WindowWidth!.Value) < 2 && Math.Abs(app.MainWindow.Height - state.Expected.WindowHeight!.Value) < 2, "Close and restart restores size");
                        Check(Math.Abs(app.MainWindow.Left - state.Expected.WindowLeft!.Value) < 2 && Math.Abs(app.MainWindow.Top - state.Expected.WindowTop!.Value) < 2, "Close and restart restores position");
                    }
                    if (state.Step == 2)
                    {
                        Check(Math.Abs(app.MainWindow.Width - Math.Min(1120, SystemParameters.WorkArea.Width)) < 2 && Math.Abs(app.MainWindow.Height - Math.Min(720, SystemParameters.WorkArea.Height)) < 2, "Disabled size uses design defaults on restart");
                        Check(Math.Abs(app.MainWindow.Left - state.Expected.WindowLeft!.Value) > 2, "Disabled position uses centered startup");
                    }
                }
                shell.Sidebar.OpenSettings(); await Task.Delay(200); app.MainWindow.UpdateLayout();
                _screenshots = Path.GetFullPath("docs/screenshots/6.2"); Directory.CreateDirectory(_screenshots);
                SaveWindow(app.MainWindow, state.Step == 0 ? "real-settings.png" : "real-settings-restarted.png");
                if (state.Step == 0)
                {
                    settings.StartupPage = StartupPage.Favorites; await settings.FlushAsync();
                    settings.RememberWindowSize = true; await settings.FlushAsync(); settings.RememberWindowPosition = true; await settings.FlushAsync();
                    app.MainWindow.Width = 940; app.MainWindow.Height = 640; app.MainWindow.Left = 60; app.MainWindow.Top = 50;
                    await OpenSettingsDirectoryForTestAsync(settings, paths);
                }
                else if (state.Step == 1)
                {
                    settings.StartupPage = StartupPage.Recent; await settings.FlushAsync();
                    settings.RememberWindowSize = false; await settings.FlushAsync(); settings.RememberWindowPosition = false; await settings.FlushAsync();
                }
                else if (state.Step == 2) { settings.StartupPage = StartupPage.Home; await settings.FlushAsync(); }
                app.MainWindow.UpdateLayout();
                await service.LoadAsync();
                state = state with { Expected = WindowPlacementService.Capture(service.Current, app.MainWindow) };
                File.WriteAllText(snapshotPath, JsonSerializer.Serialize(state));
                app.MainWindow.Close();
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); app.Shutdown(1); }
        }));
        return app.Run();
    }

    private static async Task OpenSettingsDirectoryForTestAsync(SettingsViewModel settings, ApplicationDataPaths paths)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        var before = new HashSet<int>();
        try
        {
            foreach (dynamic entry in shell.Windows()) before.Add((int)entry.HWND);
            await settings.OpenDataDirectoryCommand.ExecuteAsync(null);
            await Task.Delay(700);
            bool found = false;
            foreach (dynamic entry in shell.Windows())
            {
                if (!Uri.TryCreate((string)entry.LocationURL, UriKind.Absolute, out var uri) || !uri.IsFile ||
                    !string.Equals(uri.LocalPath.TrimEnd('\\'), paths.RootDirectory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
                found = true;
                // Close only the Explorer window created by this test, so subsequent mouse/OLE tests remain safe.
                if (!before.Contains((int)entry.HWND)) entry.Quit();
            }
            Check(found, "Windows Shell opens real data directory");
        }
        finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
    }
}
