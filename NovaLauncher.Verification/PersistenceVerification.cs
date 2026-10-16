using System.IO;
using System.Diagnostics;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using NovaLauncher.App.Models;
using NovaLauncher.App.Persistence;
using NovaLauncher.App.Services;
using NovaLauncher.App.ViewModels;
using NovaLauncher.App.Views;

namespace NovaLauncher.Verification;

internal static partial class Program
{
    private static ApplicationPersistenceService CreatePersistence(string root) => CreatePersistence(new ApplicationDataPaths(root));
    private static ApplicationPersistenceService CreatePersistence(ApplicationDataPaths paths) => new(
        new ApplicationRepository(new LauncherDbContextFactory(paths), paths),
        new ApplicationIconCache(paths, new ApplicationIconService()));

    private sealed class MemoryPersistence : IApplicationPersistenceService
    {
        public Task<ApplicationLoadResult> LoadAsync(CancellationToken token) => Task.FromResult(new ApplicationLoadResult([]));
        public Task<string?> AddAsync(LauncherApplication application, CancellationToken token) => Task.FromResult<string?>(null);
        public Task SaveStatisticsAsync(LauncherApplication application) => Task.CompletedTask;
        public Task<string?> SetCategoryAsync(Guid applicationId, Guid? categoryId, CancellationToken token) => Task.FromResult<string?>(null);
        public Task FlushAsync() => Task.CompletedTask;
    }

    private static async Task RunPersistenceAsync(MainWindow window)
    {
        await VerifyProcessRestartAsync();
        var paths = new ApplicationDataPaths(Path.Combine(_fixtures, "persistence"));
        var factory = new LauncherDbContextFactory(paths);
        var repository = new ApplicationRepository(factory, paths);
        var store = CreatePersistence(paths);
        var importer = new ApplicationImportService(new ShortcutResolver(), new ApplicationIconService());
        var launcher = new ApplicationLaunchService();
        Check(ApplicationDataPaths.Default.DatabasePath == Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "NovaLauncher", "Data", "nova.db"), "Production database is under LocalApplicationData");
        Check((await store.LoadAsync(default)).Error is null && File.Exists(paths.DatabasePath) &&
            Directory.Exists(paths.IconsDirectory), "First load creates SQLite database and cache directories");
        string exe = CopyProbe("持久化应用");
        string second = CopyProbe("快捷方式持久化");
        string shortcut = MakeConfiguredShortcut(second, _fixtures);
        string iconExe = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        Guid iconId;
        DateTimeOffset? launched;
        using (var vm = new ContentViewModel(importer, launcher, store, new ToastService()))
        {
            await vm.ImportAsync([exe, shortcut, iconExe]);
            Check(vm.Applications.Count == 3, "Three imports appear only after database save");
            Check((await repository.GetAllAsync(default)).Count == 3, "EXE and LNK records persisted");
            iconId = vm.Applications[2].Application.Id;
            Check(File.Exists(paths.IconPath(iconId)), "PNG cache uses application ID");
            await vm.Applications[0].LaunchCommand.ExecuteAsync(null);
            await vm.FlushAsync();
            launched = vm.Applications[0].LastLaunchTime;
            Check(vm.Applications[0].LaunchCount == 1 && launched is not null, "Real EXE launch updates persisted statistics");
        }
        // Recreate every service/context and the bound workspace, as on a fresh application startup.
        using (var restored = new ContentViewModel(importer, launcher, CreatePersistence(paths), new ToastService()))
        {
            await restored.InitializeAsync();
            window.DataContext = new MainWindowViewModel(restored);
            await Task.Delay(200);
            Check(restored.Applications.Count == 3 && restored.Applications[0].LaunchCount == 1 &&
                restored.Applications[0].LastLaunchTime == launched, "Fresh service graph restores cards, count and exact timestamp");
            Check(restored.Applications[2].Application.Icon is { Length: > 0 }, "Cached PNG restores icon");
            await restored.Applications[1].LaunchCommand.ExecuteAsync(null);
            Check(restored.Applications[1].LaunchCount == 1, "Restored LNK still launches via Shell");
            await restored.ImportAsync([exe.ToUpperInvariant(), MakeShortcut("同目标", exe)]);
            Check(restored.Applications.Count == 3 && (await repository.GetAllAsync(default)).Count == 3, "EXE/LNK duplicate imports remain one record");
            SaveWindow(window, "restored.png");
        }
        var original = (await repository.GetAllAsync(default))[0];
        bool duplicate = false;
        try { await repository.AddAsync(new ApplicationEntity { Id = Guid.NewGuid(), DisplayName = "duplicate",
            ExecutablePath = original.ExecutablePath.ToUpperInvariant(), SourcePath = original.SourcePath, DateAdded = DateTimeOffset.UtcNow }, default); }
        catch (DbUpdateException) { duplicate = true; }
        Check(duplicate && (await repository.GetAllAsync(default)).Count == 3, "Database unique index rejects direct duplicate bypassing UI");
        File.Delete(paths.IconPath(iconId));
        Check((await CreatePersistence(paths).LoadAsync(default)).Applications.Single(x => x.Id == iconId).Icon is { Length: > 0 }
            && File.Exists(paths.IconPath(iconId)), "Deleted icon cache is re-extracted");
        File.WriteAllText(paths.IconPath(iconId), "corrupt PNG");
        Check((await CreatePersistence(paths).LoadAsync(default)).Applications.Single(x => x.Id == iconId).Icon is { Length: > 0 }, "Corrupt PNG is re-extracted");
        await Task.Delay(500);
        File.Delete(exe);
        var missing = await CreatePersistence(paths).LoadAsync(default);
        Check(missing.Applications.Count == 3 && !missing.Applications[0].IsAvailable, "Missing EXE remains visible and unavailable; others load");
        Check(!(await launcher.LaunchAsync(missing.Applications[0])).Succeeded, "Missing target reports launch failure safely");
        await repository.UpdateAsync(original.Id, 10, launched, default);
        await repository.UpdateAsync(original.Id, 2, launched, default);
        Check((await repository.GetAllAsync(default))[0].LaunchCount == 10, "Older statistics snapshot cannot overwrite newer count");
        using (var db = factory.CreateDbContext())
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Applications SET LastLaunchTime = 'invalid-date' WHERE Id = {original.Id}");
        Check((await CreatePersistence(paths).LoadAsync(default)).Applications.Count == 2,
            "One malformed database record does not block healthy records");

        var brokenPaths = new ApplicationDataPaths(Path.Combine(_fixtures, "corrupt"));
        Directory.CreateDirectory(Path.GetDirectoryName(brokenPaths.DatabasePath)!);
        File.WriteAllText(brokenPaths.DatabasePath, "not sqlite");
        using (var broken = new ContentViewModel(importer, launcher, CreatePersistence(brokenPaths), new ToastService()))
        {
            await broken.InitializeAsync();
            Check(broken.StatusMessage is not null, "Corrupt database yields lightweight error");
            await broken.ImportAsync([iconExe]);
            Check(broken.Applications.Count == 0 && broken.StatusMessage is not null, "Save failure leaves no ghost card");
        }
        string blockedRoot = Path.Combine(_fixtures, "blocked");
        File.WriteAllText(blockedRoot, "directory unavailable");
        Check((await CreatePersistence(blockedRoot).LoadAsync(default)).Error is not null, "Unavailable data directory safely fails");
        var cachePaths = new ApplicationDataPaths(Path.Combine(_fixtures, "cache-failure"));
        Directory.CreateDirectory(Path.GetDirectoryName(cachePaths.IconsDirectory)!);
        File.WriteAllText(cachePaths.IconsDirectory, "blocked cache");
        var cacheStore = CreatePersistence(cachePaths);
        var model = (await importer.ImportAsync(iconExe, default)).Application!;
        Check(await cacheStore.AddAsync(model, default) is null &&
            (await cacheStore.LoadAsync(default)).Applications.Single().Icon is { Length: > 0 }, "Cache write failure preserves database record and extracted icon");
        var fake = new ControlledLauncher();
        fake.Reset(new(true));
        using var statistics = new ApplicationCardViewModel(model, fake, CreatePersistence(brokenPaths));
        bool launchFailed = false;
        statistics.LaunchFailed += _ => launchFailed = true;
        await statistics.LaunchCommand.ExecuteAsync(null);
        Check(statistics.LaunchCount == 1 && !launchFailed, "Statistics write failure cannot turn launch success into failure");
    }

    private static async Task VerifyProcessRestartAsync()
    {
        string root = Path.Combine(_fixtures, "restart");
        Directory.CreateDirectory(root);
        string exe = CopyProbe("重启测试EXE");
        string linkTarget = CopyProbe("重启测试LNK");
        string link = MakeConfiguredShortcut(linkTarget, _fixtures);
        File.WriteAllLines(Path.Combine(root, "inputs.txt"), [exe, link, Path.Combine(Environment.SystemDirectory, "notepad.exe")]);
        foreach (string phase in new[] { "--restart-write", "--restart-read", "--restart-missing" })
        {
            if (phase == "--restart-missing") File.Delete(exe);
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(phase);
            start.ArgumentList.Add(root);
            using var child = Process.Start(start)!;
            Task<string> stdout = child.StandardOutput.ReadToEndAsync();
            Task<string> stderr = child.StandardError.ReadToEndAsync();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Check(child.ExitCode == 0, $"Independent WPF process {phase}: {await stdout} {await stderr}");
        }
    }

    private static int RunRestartProcess(string phase, string root)
    {
        var paths = new ApplicationDataPaths(Path.Combine(root, "data"));
        var app = new NovaLauncher.App.App(CreatePersistence(paths));
        app.InitializeComponent();
        app.Startup += (_, _) => app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                var vm = ((MainWindowViewModel)app.MainWindow.DataContext).Content;
                await vm.InitializeAsync();
                if (phase == "--restart-write")
                {
                    await vm.ImportAsync(File.ReadAllLines(Path.Combine(root, "inputs.txt")));
                    Check(vm.Applications.Count == 3, "imported three applications");
                    await vm.Applications[0].LaunchCommand.ExecuteAsync(null);
                    _ = vm.Applications[1].LaunchCommand.ExecuteAsync(null);
                    File.WriteAllText(Path.Combine(root, "timestamp.txt"), vm.Applications[0].LastLaunchTime!.Value.ToString("O"));
                }
                else
                {
                    Check(vm.Applications.Count == 3, "cards restored after process exit");
                    Check(vm.Applications[0].LaunchCount == 1 && vm.Applications[0].LastLaunchTime ==
                        DateTimeOffset.Parse(File.ReadAllText(Path.Combine(root, "timestamp.txt"))), "count and exact time restored");
                    Check(vm.Applications[2].Application.Icon is { Length: > 0 }, "icon restored");
                    if (phase == "--restart-read")
                    {
                        await vm.Applications[1].LaunchCommand.ExecuteAsync(null);
                        Check(vm.Applications[1].LaunchCount == 2, "restored LNK launches");
                    }
                    else Check(!vm.Applications[0].IsAvailable, "missing executable retained");
                }
                app.MainWindow.Close(); // Exercises App's asynchronous close/flush lifecycle.
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); app.Shutdown(1); }
        }));
        return app.Run();
    }
}
