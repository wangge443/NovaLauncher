using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.EntityFrameworkCore;
using NovaLauncher.App.Models;
using NovaLauncher.App.Persistence;
using NovaLauncher.App.Services;
using NovaLauncher.App.ViewModels;
using NovaLauncher.App.Views;

namespace NovaLauncher.Verification;

internal static partial class Program
{
    private static ApplicationCardViewModel[] RecentCards(ContentViewModel vm) => vm.FilteredApplications.Cast<ApplicationCardViewModel>().ToArray();
    private static ContentViewModel RecentContent(ApplicationDataPaths paths) => new(
        new ApplicationImportService(new ShortcutResolver(), new ApplicationIconService()),
        new ApplicationLaunchService(), CreatePersistence(paths), new ToastService(), CategoryStore(paths));

    private static async Task RunRecentAsync(MainWindow window)
    {
        string root = Path.Combine(_fixtures, "recent");
        var paths = new ApplicationDataPaths(root);
        using var vm = RecentContent(paths);
        var shell = new MainWindowViewModel(vm);
        window.DataContext = shell;
        await vm.InitializeAsync();
        string output = Path.Combine(_fixtures, "recent-probes");
        Directory.CreateDirectory(output);
        string? previous = Environment.GetEnvironmentVariable("NOVA_LAUNCH_TEST_OUTPUT");
        Environment.SetEnvironmentVariable("NOVA_LAUNCH_TEST_OUTPUT", output);
        try
        {
            string aPath = CopyProbe("Recent A"), bPath = CopyProbe("Recent B"), cPath = CopyProbe("Recent C");
            await vm.ImportAsync([aPath, bPath, cPath]);
            var a = vm.Applications[0]; var b = vm.Applications[1]; var c = vm.Applications[2];
            var sourceOrder = vm.Applications.ToArray();
            var sidebar = Descendants<SidebarView>(window).Single();
            var footer = Descendants<ListBox>(sidebar).Single(x => x.ItemsSource == shell.Sidebar.FooterItems);
            footer.SelectedItem = shell.Sidebar.FooterItems[1];
            Check(vm.Mode == WorkspaceMode.Recent && vm.IsEmpty, "Recent Sidebar selects only launched applications");
            Check(vm.EmptyTitle == "最近还没有启动过应用" && vm.EmptyHint == "启动应用后，它会出现在这里", "Recent has a dedicated empty state");
            window.UpdateLayout();
            SaveWindow(window, "recent-empty.png");
            shell.Sidebar.SelectedItem = shell.Sidebar.Items[0];
            await a.LaunchCommand.ExecuteAsync(null);
            shell.Sidebar.SelectedItem = shell.Sidebar.FooterItems[1];
            Check(RecentCards(vm).SequenceEqual([a]) && a.LaunchCount == 1, "First successful Home launch enters Recent");
            await b.LaunchCommand.ExecuteAsync(null);
            Check(RecentCards(vm).SequenceEqual([b, a]), "Second successful launch sorts first without navigation");
            await c.LaunchCommand.ExecuteAsync(null);
            Check(RecentCards(vm).SequenceEqual([c, b, a]), "A B C launches produce C B A");
            var game = vm.CategoryManager.Categories.First();
            await a.ChangeCategoryCommand.ExecuteAsync(new(game.Id, game.Name));
            await a.ToggleFavoriteCommand.ExecuteAsync(null);
            await a.LaunchCommand.ExecuteAsync(null);
            Check(RecentCards(vm).SequenceEqual([a, c, b]) && a.LaunchCount == 2, "Recent relaunch immediately moves A first and increments count");
            Check(a.IsFavorite && a.CategoryId == game.Id, "Recent launch preserves favorite and category");
            Check(a.LastLaunchText.Contains("刚刚"), "Recent hover reuses existing time formatter");
            Check(vm.Applications.SequenceEqual(sourceOrder), "Recent sorting never mutates source collection");
            await WaitForAsync(() => ReadProbes(output).Length == 4);
            Check(ReadProbes(output).Length == 4, "Four successful launches reach real test processes");
            window.UpdateLayout();
            SaveWindow(window, "recent-order.png");
            var cardView = Descendants<ApplicationCardView>(window).First(x => x.DataContext == a);
            Check(cardView.ActualWidth == 136 && cardView.ActualHeight == 136, "Recent reuses existing fixed-size card");
            shell.Sidebar.SelectedItem = shell.Sidebar.Items[0];
            Check(RecentCards(vm).SequenceEqual(sourceOrder) && ((ListCollectionView)vm.FilteredApplications).CustomSort is null, "Home clears Recent sort and restores source order");
            Check(footer.SelectedItem is null, "Leaving Recent clears footer selection");
            shell.Sidebar.SelectedItem = shell.Sidebar.FooterItems[1];
            shell.Sidebar.SelectedItem = game;
            Check(vm.Mode == WorkspaceMode.Category && RecentCards(vm).SequenceEqual([a]) && ((ListCollectionView)vm.FilteredApplications).CustomSort is null, "Recent to Category applies only Category filter");
            shell.Sidebar.SelectedItem = shell.Sidebar.FooterItems[1];
            shell.Sidebar.SelectedItem = shell.Sidebar.FooterItems[0];
            Check(vm.Mode == WorkspaceMode.Favorites && RecentCards(vm).SequenceEqual([a]) && vm.SelectedCategory is null, "Recent to Favorites applies only favorite filter");
            shell.Sidebar.SelectedItem = shell.Sidebar.FooterItems[1];
            await vm.ImportAsync([CopyProbe("Never launched")]);
            Check(vm.Applications.Count == 4 && RecentCards(vm).SequenceEqual([a, c, b]), "Recent import remains hidden until successful launch");
            await vm.FlushAsync();
            string snapshotFile = Path.Combine(_fixtures, "recent-restart.json");
            File.WriteAllText(snapshotFile, JsonSerializer.Serialize(SnapshotRecent(root, vm)));
            await RunCategoryChildAsync("--recent-restart", snapshotFile);
            await Task.Delay(300);
            File.Delete(bPath);
            using var missing = RecentContent(paths);
            await missing.InitializeAsync();
            missing.SelectWorkspace(WorkspaceMode.Recent);
            var unavailable = missing.Applications.Single(x => x.Application.Id == b.Application.Id);
            Check(!unavailable.IsAvailable && RecentCards(missing).Contains(unavailable), "Unavailable historical application remains in Recent");
            var oldTime = unavailable.LastLaunchTime;
            long oldCount = unavailable.LaunchCount;
            var beforeFailed = RecentCards(missing);
            await unavailable.LaunchCommand.ExecuteAsync(null);
            Check(unavailable.LastLaunchTime == oldTime && unavailable.LaunchCount == oldCount && RecentCards(missing).SequenceEqual(beforeFailed), "Failed launch does not change Recent order or statistics");
            using (var db = new LauncherDbContextFactory(paths).CreateDbContext())
            {
                Guid id = c.Application.Id;
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Applications SET LastLaunchTime = 'invalid-time' WHERE Id = {id}");
            }
            using var recovered = RecentContent(paths);
            await recovered.InitializeAsync();
            recovered.SelectWorkspace(WorkspaceMode.Recent);
            Check(recovered.Applications.Count == 3 && RecentCards(recovered).Length == 2, "Malformed timestamp skips damaged record without crashing Recent");
            await VerifyRecentTiesAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable("NOVA_LAUNCH_TEST_OUTPUT", previous);
            foreach (var probe in ReadProbes(output))
            {
                try { using var process = Process.GetProcessById(probe.ProcessId); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (ArgumentException) { }
            }
        }
    }

    private sealed class RecentMemoryStore(IReadOnlyList<LauncherApplication> records) : IApplicationPersistenceService
    {
        public int Loads { get; private set; }
        public Task<ApplicationLoadResult> LoadAsync(CancellationToken token) { Loads++; return Task.FromResult(new ApplicationLoadResult(records)); }
        public Task<string?> AddAsync(LauncherApplication application, CancellationToken token) => Task.FromResult<string?>(null);
        public Task SaveStatisticsAsync(LauncherApplication application) => Task.CompletedTask;
        public Task<string?> SetCategoryAsync(Guid id, Guid? categoryId, CancellationToken token) => Task.FromResult<string?>(null);
        public Task<string?> SetFavoriteAsync(Guid id, bool favorite, CancellationToken token) => Task.FromResult<string?>(null);
        public Task FlushAsync() => Task.CompletedTask;
    }

    private static async Task VerifyRecentTiesAsync()
    {
        var time = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        var records = Enumerable.Range(0, 500).Select(index => new LauncherApplication(
            Guid.Parse($"00000000-0000-0000-0000-{index + 1:D12}"), index % 2 == 0 ? "B" : "A", $@"C:\Recent\{index}.exe", $@"C:\Recent\{index}.exe", null, ApplicationSourceType.Executable)
            { LastLaunchTime = time.ToOffset(index % 2 == 0 ? TimeSpan.FromHours(-8) : TimeSpan.FromHours(2)), LaunchCount = 1, IsFavorite = index % 2 == 0 }).ToArray();
        var store = new RecentMemoryStore(records);
        using var vm = new ContentViewModel(new ApplicationImportService(new ShortcutResolver(), new ApplicationIconService()), new ApplicationLaunchService(), store, new ToastService());
        await vm.InitializeAsync();
        var expected = vm.Applications.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Application.Id).ToArray();
        var timer = Stopwatch.StartNew();
        bool stable = true;
        for (int index = 0; index < 20; index++)
        {
            vm.SelectWorkspace(WorkspaceMode.Recent);
            stable &= RecentCards(vm).SequenceEqual(expected);
            vm.SelectWorkspace(WorkspaceMode.Favorites);
            vm.SelectWorkspace(WorkspaceMode.AllApplications);
        }
        timer.Stop();
        Check(stable, "Equal instants across offsets use deterministic name/Id ties across repeated switches");
        Check(store.Loads == 1 && vm.Applications.Select(x => x.Application).SequenceEqual(records), "500-card mode switches do not reload storage or reorder source");
        Console.WriteLine($"500-card, 60 mode switches: {timer.ElapsedMilliseconds} ms (view model only)");
    }

    private sealed record RecentState(Guid Id, long Count, DateTimeOffset? Time, Guid? Category, bool Favorite);
    private sealed record RecentSnapshot(string Root, Guid[] RecentIds, RecentState[] States);
    private static RecentSnapshot SnapshotRecent(string root, ContentViewModel vm) => new(root,
        RecentCards(vm).Select(x => x.Application.Id).ToArray(),
        vm.Applications.Select(x => new RecentState(x.Application.Id, x.LaunchCount, x.LastLaunchTime, x.CategoryId, x.IsFavorite)).ToArray());

    private static async Task<int> VerifyRealRecentAsync()
    {
        var paths = ApplicationDataPaths.Default;
        string snapshot = Path.Combine(Path.GetTempPath(), "NovaLauncher-recent-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Directory.CreateDirectory(paths.BackupsDirectory);
            string backup = Path.Combine(paths.BackupsDirectory, $"nova-before-recent-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}.db");
            using (new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            using (var source = OpenDatabase(paths.DatabasePath, true))
            using (var destination = OpenDatabase(backup)) source.BackupDatabase(destination);
            Console.WriteLine("Backup: " + backup);
            await RunCategoryChildAsync("--recent-real-run", snapshot);
            await RunCategoryChildAsync("--recent-real-check", snapshot);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { File.Delete(snapshot); }
    }

    private static int RunRecentWindow(string snapshotPath, bool real, bool launch)
    {
        RecentSnapshot? snapshot = launch ? null : JsonSerializer.Deserialize<RecentSnapshot>(File.ReadAllText(snapshotPath))!;
        var paths = real ? ApplicationDataPaths.Default : new ApplicationDataPaths(snapshot!.Root);
        var app = real ? new NovaLauncher.App.App() : new NovaLauncher.App.App(CreatePersistence(paths), CategoryStore(paths));
        app.InitializeComponent();
        app.Startup += (_, _) => app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                var shell = (MainWindowViewModel)app.MainWindow.DataContext;
                var vm = shell.Content;
                await vm.InitializeAsync();
                Check(vm.StatusMessage is null, "Recent window restores applications successfully");
                if (real)
                {
                    _screenshots = Path.GetFullPath("docs/screenshots/5.3");
                    Directory.CreateDirectory(_screenshots);
                }
                if (launch)
                {
                    if (vm.Applications.Count(x => x.IsAvailable) < 3)
                        await vm.ImportAsync([Path.Combine(Environment.SystemDirectory, "notepad.exe")]);
                    var targets = vm.Applications.Where(x => x.IsAvailable).Take(3).ToArray();
                    Check(targets.Length == 3, "Three real available applications selected");
                    var state = targets.Select(x => (x.CategoryId, x.IsFavorite, x.LaunchCount)).ToArray();
                    shell.Sidebar.SelectedItem = shell.Sidebar.FooterItems[1];
                    for (int index = 0; index < 3; index++)
                    {
                        await targets[index].LaunchCommand.ExecuteAsync(null);
                        Check(targets[index].LaunchCount == state[index].LaunchCount + 1, "Real launch succeeded: " + targets[index].DisplayName);
                    }
                    Check(RecentCards(vm).Take(3).SequenceEqual(targets.Reverse()), "Real A B C launch order becomes C B A");
                    app.MainWindow.UpdateLayout();
                    await Task.Delay(220);
                    SaveWindow(app.MainWindow, "real-recent-cba.png");
                    await targets[0].LaunchCommand.ExecuteAsync(null);
                    Check(RecentCards(vm).Take(3).SequenceEqual(new[] { targets[0], targets[2], targets[1] }), "Real A relaunch immediately becomes A C B");
                    Check(targets.Select(x => (x.CategoryId, x.IsFavorite)).SequenceEqual(state.Select(x => (x.CategoryId, x.IsFavorite))), "Real launches preserve category and favorite state");
                    await vm.FlushAsync();
                    snapshot = SnapshotRecent(Path.GetDirectoryName(Path.GetDirectoryName(paths.DatabasePath)!)!, vm);
                    File.WriteAllText(snapshotPath, JsonSerializer.Serialize(snapshot));
                    Console.WriteLine("Recent order: " + string.Join(" → ", RecentCards(vm).Select(x => x.DisplayName)));
                }
                else
                {
                    shell.Sidebar.SelectedItem = shell.Sidebar.FooterItems[1];
                    Check(RecentCards(vm).Select(x => x.Application.Id).SequenceEqual(snapshot!.RecentIds), "Independent restart restores exact Recent order from SQLite");
                    Check(SnapshotRecent(snapshot.Root, vm).States.SequenceEqual(snapshot.States), "Restart preserves exact timestamps, counts, categories and favorites");
                }
                if (real)
                {
                    app.MainWindow.UpdateLayout();
                    await Task.Delay(220);
                    SaveWindow(app.MainWindow, launch ? "real-recent-acb.png" : "real-recent-restarted.png");
                }
                app.MainWindow.Close();
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); app.Shutdown(1); }
        }));
        return app.Run();
    }
}
