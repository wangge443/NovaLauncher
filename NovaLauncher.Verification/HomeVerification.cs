using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using NovaLauncher.App.Models;
using NovaLauncher.App.Persistence;
using NovaLauncher.App.Services;
using NovaLauncher.App.ViewModels;
using NovaLauncher.App.Views;

namespace NovaLauncher.Verification;

internal static partial class Program
{
    private static ApplicationCardView[] AllCardViews(DependencyObject root)
    {
        var home = Descendants<HomeView>(root).FirstOrDefault();
        return Descendants<ApplicationCardView>(home is null ? root : (ItemsControl)home.FindName("AllApplications")).ToArray();
    }

    private static void InvokeHomeButton(HomeView view, string id)
    {
        var button = Descendants<Button>(view).Single(x => AutomationProperties.GetAutomationId(x) == id);
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    }

    private static async Task RunHomeAsync(MainWindow window)
    {
        var paths = new ApplicationDataPaths(Path.Combine(_fixtures, "home"));
        using var vm = RecentContent(paths);
        var shell = new MainWindowViewModel(vm);
        window.DataContext = shell;
        await vm.InitializeAsync();
        window.UpdateLayout();
        var home = Descendants<HomeView>(window).Single();
        Check(vm.IsHomeDashboard && !vm.Home.HasApplications && vm.IsEmpty, "Empty Home retains drop hint and hides all sections");
        SaveWindow(window, "home-empty.png");
        string output = Path.Combine(_fixtures, "home-probes");
        Directory.CreateDirectory(output);
        string? previous = Environment.GetEnvironmentVariable("NOVA_LAUNCH_TEST_OUTPUT");
        Environment.SetEnvironmentVariable("NOVA_LAUNCH_TEST_OUTPUT", output);
        try
        {
            await vm.ImportAsync(Enumerable.Range(1, 8).Select(index => CopyProbe("Home " + index)).ToArray());
            window.UpdateLayout();
            Check(vm.Home.AllApplications.Count == 8 && !vm.Home.HasRecent && !vm.Home.HasFavorites, "Imported unlaunched apps show only All Applications");
            Check(((FrameworkElement)home.FindName("RecentSection")).Visibility == Visibility.Collapsed &&
                ((FrameworkElement)home.FindName("FavoriteSection")).Visibility == Visibility.Collapsed, "Empty sections leave no headers or spacing");
            SaveWindow(window, "home-all-only.png");
            var first = vm.Applications[0];
            var allView = AllCardViews(window)[0];
            await first.ToggleFavoriteCommand.ExecuteAsync(null);
            Check(vm.Home.HasFavorites && !vm.Home.HasRecent && ReferenceEquals(vm.Home.FavoriteApplications[0], first), "First favorite instantly adds Favorite section with same card instance");
            await first.ToggleFavoriteCommand.ExecuteAsync(null);
            Check(!vm.Home.HasFavorites && vm.Home.AllApplications.Contains(first), "Last unfavorite hides Favorite section but keeps All card");
            await first.LaunchCommand.ExecuteAsync(null);
            Check(vm.Home.HasRecent && !vm.Home.HasFavorites && ReferenceEquals(vm.Home.RecentApplications[0], first), "First launch instantly adds Recent section");
            foreach (var card in vm.Applications.Skip(1)) await card.LaunchCommand.ExecuteAsync(null);
            Check(vm.Home.RecentApplications.Count == 6 && vm.Home.RecentApplications.SequenceEqual(vm.Applications.Reverse().Take(6)), "Recent Top 6 follows descending launch times");
            await first.LaunchCommand.ExecuteAsync(null);
            Check(vm.Home.RecentApplications[0] == first && vm.Home.AllApplications[0] == first && first.LaunchCount == 2, "Relaunch moves Recent first without changing All order");
            foreach (var card in vm.Applications) await card.ToggleFavoriteCommand.ExecuteAsync(null);
            Check(vm.Home.FavoriteApplications.SequenceEqual(vm.Applications.Take(6)), "Favorites Top 6 retains source order");
            window.UpdateLayout();
            Check(ReferenceEquals(AllCardViews(window)[0], allView), "Projection updates keep All Applications controls alive");
            Check(Descendants<ApplicationCardView>(home).Count(x => x.DataContext == first) == 3, "Three views share one existing application ViewModel");
            var copies = Descendants<ApplicationCardView>(home).Where(x => x.DataContext == first).ToArray();
            copies[0].ContextMenu.PlacementTarget = copies[0]; copies[0].ContextMenu.IsOpen = true;
            await Task.Delay(150);
            Check(copies.Count(x => x.ContextMenu.IsOpen) == 1, "Shared card opens only the clicked view's context menu");
            copies[0].ContextMenu.IsOpen = false;
            var game = vm.CategoryManager.Categories.First();
            await first.ChangeCategoryCommand.ExecuteAsync(new(game.Id, game.Name));
            Check(copies.All(x => ((ApplicationCardViewModel)x.DataContext).CategoryId == game.Id), "Category state synchronizes across all section copies");
            await vm.Home.FavoriteApplications[0].ToggleFavoriteCommand.ExecuteAsync(null);
            Check(!vm.Home.FavoriteApplications.Contains(first) && vm.Home.FavoriteApplications.Count == 6 && vm.Home.AllApplications.Contains(first), "Unfavorite removes projection and backfills seventh without removing All");
            window.UpdateLayout();
            InvokeHomeButton(home, "HomeShowRecent");
            await WaitForAsync(() => vm.Mode == WorkspaceMode.Recent);
            Check(shell.Sidebar.SelectedItem == shell.Sidebar.FooterItems[1] && !vm.IsHomeDashboard && RecentCards(vm).Length == 8, "View all Recent synchronizes Sidebar and exposes full list");
            shell.Sidebar.SelectedItem = shell.Sidebar.Items[0];
            window.UpdateLayout();
            home = Descendants<HomeView>(window).Single();
            InvokeHomeButton(home, "HomeShowFavorites");
            await WaitForAsync(() => vm.Mode == WorkspaceMode.Favorites);
            Check(shell.Sidebar.SelectedItem == shell.Sidebar.FooterItems[0] && RecentCards(vm).Length == 7, "View all Favorites synchronizes Sidebar and exposes full list");
            shell.Sidebar.SelectedItem = game;
            Check(RecentCards(vm).SequenceEqual([first]) && !vm.IsHomeDashboard, "Category continues using existing filtered page");
            shell.Sidebar.Profile.OpenSettingsCommand.Execute(null);
            window.UpdateLayout();
            Check(vm.Mode == WorkspaceMode.Settings && !Descendants<HomeView>(window).Any() && !AllCardViews(window).Any(), "Profile settings opens placeholder without Home cards");
            shell.Sidebar.SelectedItem = shell.Sidebar.Items[0];
            await vm.ImportAsync([CopyProbe("New Home Import")]);
            Check(vm.Home.AllApplications.Count == 9 && !vm.Home.RecentApplications.Contains(vm.Applications.Last()) &&
                !vm.Home.FavoriteApplications.Contains(vm.Applications.Last()), "New import immediately enters All only");
            window.UpdateLayout();
            home = Descendants<HomeView>(window).Single();
            Check(Descendants<ScrollViewer>(home).Count() == 1, "Home has exactly one main vertical ScrollViewer");
            SaveWindow(window, "home-sections.png");
            window.Width = 800;
            window.Height = 560;
            window.UpdateLayout();
            var grid = (ItemsControl)home.FindName("RecentApplications");
            var cards = Descendants<ApplicationCardView>(grid).ToArray();
            Check(cards.Select(x => Math.Round(x.TranslatePoint(new Point(), grid).Y)).Distinct().Count() > 1, "Narrow Home wraps Top 6 instead of fixed columns");
            var sidebarColumn = (ColumnDefinition)window.FindName("SidebarColumn");
            sidebarColumn.Width = new GridLength(270);
            window.UpdateLayout();
            var scroll = (ScrollViewer)home.FindName("HomeScroll");
            Check(scroll.ScrollableWidth == 0 && scroll.ScrollableHeight > 0, "Sidebar resize keeps vertical scrolling without horizontal overflow");
            scroll.ScrollToEnd();
            window.UpdateLayout();
            Check(scroll.VerticalOffset > 0, "Single Home scroller reaches All Applications");
            SaveWindow(window, "home-small.png");
            await vm.FlushAsync();
            await WaitForAsync(() => ReadProbes(output).Length == 9);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NOVA_LAUNCH_TEST_OUTPUT", previous);
            foreach (var record in ReadProbes(output))
            {
                try { using var process = Process.GetProcessById(record.ProcessId); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (ArgumentException) { }
            }
        }
        await VerifyLargeHomeAsync(window);
    }

    private static async Task VerifyLargeHomeAsync(MainWindow window)
    {
        var time = DateTimeOffset.UtcNow;
        var records = Enumerable.Range(0, 300).Select(index => new LauncherApplication(Guid.NewGuid(), "应用 " + index,
            $@"C:\Home\{index}.exe", $@"C:\Home\{index}.exe", null, ApplicationSourceType.Executable)
            { IsFavorite = index % 2 == 0, LastLaunchTime = index % 3 == 0 ? time.AddMinutes(-index) : null }).ToArray();
        var store = new RecentMemoryStore(records);
        using var vm = new ContentViewModel(new ApplicationImportService(new ShortcutResolver(), new ApplicationIconService()),
            new ApplicationLaunchService(), store, new ToastService());
        await vm.InitializeAsync();
        var timer = Stopwatch.StartNew();
        window.DataContext = new MainWindowViewModel(vm);
        window.UpdateLayout();
        timer.Stop();
        var home = Descendants<HomeView>(window).Single();
        var scroll = (ScrollViewer)home.FindName("HomeScroll");
        Check(vm.Home.RecentApplications.Count == 6 && vm.Home.FavoriteApplications.Count == 6 && AllCardViews(window).Length == 300, "300-card Home renders all cards with bounded projections");
        scroll.ScrollToEnd();
        window.UpdateLayout();
        Check(scroll.VerticalOffset > 0 && scroll.ScrollableWidth == 0, "300-card Home scrolls vertically without horizontal overflow");
        var cardsToDispose = vm.Applications.ToArray();
        var removed = vm.Applications[0];
        vm.Applications.RemoveAt(0);
        Check(!vm.Home.AllApplications.Contains(removed) && !vm.Home.RecentApplications.Contains(removed) && !vm.Home.FavoriteApplications.Contains(removed), "Source removal updates all projections and subscriptions");
        vm.Applications.Move(1, 0);
        Check(vm.Home.AllApplications[0] == vm.Applications[0], "Source ordering changes propagate without cloning");
        vm.Applications.Clear();
        window.UpdateLayout();
        Check(!vm.Home.HasApplications && !vm.Home.HasFavorites && !vm.Home.HasRecent && store.Loads == 1, "Source reset hides sections without any extra database read");
        Check(Descendants<TextBlock>(window).Single(x => AutomationProperties.GetAutomationId(x) == "DropHint").IsVisible, "Reset to zero restores visible Home drop empty state");
        Console.WriteLine($"300-card Home initial layout: {timer.ElapsedMilliseconds} ms");
        foreach (var card in cardsToDispose) card.Dispose();
    }

    private sealed record HomeSnapshot(Guid[] Recent, Guid[] Favorites, RecentState[] States);
    private static HomeSnapshot SnapshotHome(ContentViewModel vm) => new(vm.Home.RecentApplications.Select(x => x.Application.Id).ToArray(),
        vm.Home.FavoriteApplications.Select(x => x.Application.Id).ToArray(),
        vm.Applications.Select(x => new RecentState(x.Application.Id, x.LaunchCount, x.LastLaunchTime, x.CategoryId, x.IsFavorite)).ToArray());

    private static async Task<int> VerifyRealHomeAsync()
    {
        var paths = ApplicationDataPaths.Default;
        string snapshot = Path.Combine(Path.GetTempPath(), "NovaLauncher-home-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Directory.CreateDirectory(paths.BackupsDirectory);
            string backup = Path.Combine(paths.BackupsDirectory, $"nova-before-home-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}.db");
            using (new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            using (var source = OpenDatabase(paths.DatabasePath, true))
            using (var destination = OpenDatabase(backup)) source.BackupDatabase(destination);
            Console.WriteLine("Backup: " + backup);
            await RunCategoryChildAsync("--home-real-run", snapshot);
            await RunCategoryChildAsync("--home-real-check", snapshot);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { File.Delete(snapshot); }
    }

    private static int RunHomeWindow(string snapshotFile, bool exercise)
    {
        var app = new NovaLauncher.App.App();
        app.InitializeComponent();
        app.Startup += (_, _) => app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                var shell = (MainWindowViewModel)app.MainWindow.DataContext;
                var vm = shell.Content;
                await vm.InitializeAsync();
                Check(vm.StatusMessage is null && vm.IsHomeDashboard && vm.Home.AllApplications.Count >= 3, "Real Home restores existing QQ, WeChat and Notepad data");
                if (exercise)
                {
                    var target = vm.Applications.Single(x => Path.GetFileName(x.Application.ExecutablePath).Equals("notepad.exe", StringComparison.OrdinalIgnoreCase));
                    long count = target.LaunchCount;
                    var states = vm.Applications.Select(x => (x.Application.Id, x.CategoryId, x.IsFavorite)).ToArray();
                    await target.LaunchCommand.ExecuteAsync(null);
                    Check(target.LaunchCount == count + 1 && vm.Home.RecentApplications[0] == target, "Real Home launch updates Recent instantly");
                    bool original = target.IsFavorite;
                    await target.ToggleFavoriteCommand.ExecuteAsync(null);
                    Check(target.IsFavorite != original && vm.Home.FavoriteApplications.Contains(target) == target.IsFavorite, "Real Home star updates Favorite projection instantly");
                    await target.ToggleFavoriteCommand.ExecuteAsync(null);
                    Check(target.IsFavorite == original && vm.Home.AllApplications.Contains(target), "Real Home unfavorite restores original state and keeps All");
                    Check(vm.Applications.Select(x => (x.Application.Id, x.CategoryId, x.IsFavorite)).SequenceEqual(states), "Real category and favorite states unchanged after round trip");
                    await vm.FlushAsync();
                    File.WriteAllText(snapshotFile, JsonSerializer.Serialize(SnapshotHome(vm)));
                }
                else if (snapshotFile.Length > 0)
                {
                    var expected = JsonSerializer.Deserialize<HomeSnapshot>(File.ReadAllText(snapshotFile))!;
                    var actual = SnapshotHome(vm);
                    Check(actual.Recent.SequenceEqual(expected.Recent) && actual.Favorites.SequenceEqual(expected.Favorites), "Restart restores both Home projections from persisted data");
                    Check(actual.States.SequenceEqual(expected.States), "Restart preserves statistics, favorites and categories exactly");
                }
                _screenshots = Path.GetFullPath("docs/screenshots/5.4");
                Directory.CreateDirectory(_screenshots);
                app.MainWindow.UpdateLayout();
                await Task.Delay(220);
                SaveWindow(app.MainWindow, snapshotFile.Length == 0 ? "real-home-preview.png" : exercise ? "real-home.png" : "real-home-restarted.png");
                app.MainWindow.Close();
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); app.Shutdown(1); }
        }));
        return app.Run();
    }
}
