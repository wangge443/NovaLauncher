using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NovaLauncher.App.Models;
using NovaLauncher.App.Persistence;

namespace NovaLauncher.Verification;

internal static partial class Program
{
    private static async Task<int> RunReleaseSafetyAsync()
    {
        using var trace = new System.Diagnostics.ConsoleTraceListener();
        System.Diagnostics.Trace.Listeners.Add(trace);
        string root = Path.Combine(Path.GetTempPath(), "NovaLauncher-ReleaseSafety-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new ApplicationDataPaths(root);
            await CreatePersistence(paths).LoadAsync(default);
            var first = SettingsService(paths); var second = SettingsService(paths);
            await first.LoadAsync(); await second.LoadAsync();
            Check(await first.UpdateAsync(x => x with { StartupPage = StartupPage.Favorites, BackgroundOverlayOpacity = .81 }), "First settings writer saves startup and background");
            Check(await second.UpdateAsync(x => x with { WindowWidth = 940, WindowHeight = 640 }), "Stale settings writer saves window geometry");
            var read = SettingsService(paths); await read.LoadAsync();
            Check(read.Current.StartupPage == StartupPage.Favorites && read.Current.BackgroundOverlayOpacity == .81 && read.Current.WindowWidth == 940, "Stale window patch preserves newer background and startup page");
            Check(second.Current.StartupPage == StartupPage.Favorites, "Writer receives latest merged settings after commit");
            var writes = await Task.WhenAll(first.UpdateAsync(x => x with { RememberWindowSize = false }), second.UpdateAsync(x => x with { BackgroundBlurRadius = 21 }));
            Check(writes.All(x => x), "Concurrent settings writes both succeed");
            await read.LoadAsync();
            Check(!read.Current.RememberWindowSize && read.Current.BackgroundBlurRadius == 21 && read.Current.WindowWidth == 940, "Concurrent disjoint settings patches preserve both writers");

            var repository = new ApplicationRepository(new LauncherDbContextFactory(paths), paths);
            Guid id = Guid.NewGuid(); DateTimeOffset now = DateTimeOffset.UtcNow;
            await repository.AddAsync(new ApplicationEntity { Id = id, DisplayName = "Statistics", ExecutablePath = @"C:\Verification\probe.exe", SourcePath = @"C:\Verification\probe.exe", DateAdded = now }, default);
            await repository.UpdateAsync(id, 2, now, default);
            await repository.UpdateAsync(id, 1, now.AddMinutes(-1), default);
            using (var db = new LauncherDbContextFactory(paths).CreateDbContext())
            {
                var saved = await db.Applications.SingleAsync(x => x.Id == id);
                Check(saved.LaunchCount == 2 && saved.LastLaunchTime == now, "Late older statistics snapshot cannot roll back count or launch time");
            }
            await repository.RemoveAsync(id, default);
            await repository.UpdateAsync(id, 3, now.AddMinutes(1), default);
            using (var db = new LauncherDbContextFactory(paths).CreateDbContext())
                Check(!await db.Applications.AnyAsync(x => x.Id == id), "Statistics cannot resurrect a removed application");

            var upgradePaths = new ApplicationDataPaths(Path.Combine(root, "upgrade"));
            Directory.CreateDirectory(Path.GetDirectoryName(upgradePaths.DatabasePath)!);
            using (var db = new LauncherDbContextFactory(upgradePaths).CreateDbContext())
                await db.GetService<IMigrator>().MigrateAsync("20261017025537_AddAppSettings");
            await Initializer(upgradePaths).InitializeAsync(default);
            Check(BackupCount(upgradePaths) == 1, "Normal pending migration creates a verified pre-upgrade backup");
            using (var backup = OpenDatabase(Directory.GetFiles(upgradePaths.BackupsDirectory, "*.db").Single(), true))
                Check(Query(backup, "SELECT COUNT(*) FROM __EFMigrationsHistory") == "[[5]]", "Upgrade backup preserves pre-upgrade migration history");
            await Initializer(upgradePaths).InitializeAsync(default);
            Check(BackupCount(upgradePaths) == 1, "Already-current startup does not create repeated backups");

            string unsafeRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaLauncher");
            bool rejected = false;
            try { ApplicationDataPaths.ConfigureVerificationRoot(["--verification-data-root=" + unsafeRoot]); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected && ApplicationDataPaths.VerificationRoot is null, "Verification override refuses real user data root");
            rejected = false;
            try { ApplicationDataPaths.ConfigureVerificationRoot(["--verification-data-root=" + root]); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "Verification override requires an explicit marker");
            Console.WriteLine($"PASS: {_checks} release safety assertions");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(root, true); }
    }
}
