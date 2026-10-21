using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
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
        var all = Descendants<AllApplicationsView>(root).FirstOrDefault();
        return Descendants<ApplicationCardView>(all is null ? root : (ItemsControl)all.FindName("AllApplications")).ToArray();
    }

    private static async Task RunAllApplicationsAsync(MainWindow window)
    {
        var paths = new ApplicationDataPaths(Path.Combine(_fixtures, "all-applications"));
        using var vm = RecentContent(paths);
        var shell = new MainWindowViewModel(vm);
        window.DataContext = shell;
        await vm.InitializeAsync();
        window.UpdateLayout();
        Check(vm.Mode == WorkspaceMode.AllApplications && vm.IsEmpty && !vm.ShowPageTitle, "Default empty library hides title and retains drop state");
        Check(shell.Sidebar.Items[0].Name == "全部应用" && shell.Sidebar.Items[0].Icon == "\uE80A", "Primary navigation uses All Applications and Apps icon");
        Check(shell.Sidebar.Items.Concat(shell.Sidebar.FooterItems).All(x => x.Name is not ("首页" or "设置" or "新建分类")), "Sidebar contains no Home, Settings or category creation row");
        Check(Descendants<Button>(window).Single(x => AutomationProperties.GetAutomationId(x) == "QuickSearchTrigger").IsVisible, "Search trigger remains visible in empty library");
        SaveWindow(window, "all-empty.png");
        string output = Path.Combine(_fixtures, "all-probes");
        Directory.CreateDirectory(output);
        string? previous = Environment.GetEnvironmentVariable("NOVA_LAUNCH_TEST_OUTPUT");
        Environment.SetEnvironmentVariable("NOVA_LAUNCH_TEST_OUTPUT", output);
        try
        {
            await vm.ImportAsync(Enumerable.Range(1, 8).Select(i => CopyProbe("Library " + i)).ToArray());
            window.UpdateLayout();
            var view = Descendants<AllApplicationsView>(window).Single();
            Check(vm.PageTitle == "全部应用" && vm.ShowPageTitle && AllCardViews(window).Length == 8, "Import immediately shows library title and all eight applications");
            Check(view.FindName("RecentSection") is null && view.FindName("FavoriteSection") is null && !Descendants<Button>(view).Any(x => AutomationProperties.GetAutomationId(x).StartsWith("HomeShow")), "Dashboard sections and view-all links no longer exist");
            var first = vm.Applications[0];
            var firstView = AllCardViews(window)[0];
            await first.ToggleFavoriteCommand.ExecuteAsync(null);
            await first.LaunchCommand.ExecuteAsync(null);
            window.UpdateLayout();
            Check(Descendants<ApplicationCardView>(window).Count() == 8 && ReferenceEquals(firstView, AllCardViews(window)[0]), "Favorite and launch keep one unchanged visual per application");
            Check(first.IsFavorite && first.LaunchCount == 1 && first.LastLaunchTime.HasValue, "Shared application state updates immediately");
            SaveWindow(window, "all-applications.png");
            shell.Sidebar.SelectedItem = shell.Sidebar.FooterItems[0];
            window.UpdateLayout();
            Check(vm.PageTitle == "收藏" && AllCardViews(window).Single().DataContext == first, "Favorites page displays only favorited application");
            SaveWindow(window, "favorites.png");
            await first.ToggleFavoriteCommand.ExecuteAsync(null);
            window.UpdateLayout();
            Check(vm.IsEmpty && vm.EmptyTitle == "还没有收藏应用" && vm.EmptyHint == "右键应用即可添加到收藏", "Favorites empty state matches product wording");
            shell.Sidebar.SelectedItem = shell.Sidebar.FooterItems[1];
            window.UpdateLayout();
            Check(vm.PageTitle == "最近使用" && AllCardViews(window).Single().DataContext == first, "Recent page contains only successfully launched application");
            SaveWindow(window, "recent.png");
            var category = vm.CategoryManager.Categories.First();
            await first.ChangeCategoryCommand.ExecuteAsync(new(category.Id, category.Name));
            shell.Sidebar.SelectedItem = category;
            window.UpdateLayout();
            Check(vm.PageTitle == category.Name && AllCardViews(window).Single().DataContext == first, "Category title and filter remain correct");
            shell.Sidebar.Profile.OpenSettingsCommand.Execute(null);
            window.UpdateLayout();
            Check(vm.Mode == WorkspaceMode.Settings && !Descendants<AllApplicationsView>(window).Any(), "Profile flyout Settings leaves library");
            shell.Sidebar.SelectedItem = shell.Sidebar.Items[0];
            await vm.ImportAsync([CopyProbe("Library new import")]);
            window.UpdateLayout();
            Check(AllCardViews(window).Length == 9, "Returning library immediately renders a newly imported application");
            vm.QuickLauncher.Open(); vm.QuickLauncher.Close();
            Check(vm.Mode == WorkspaceMode.AllApplications, "Quick Launcher open and close preserve library navigation");
            window.Width = 800; window.Height = 560;
            ((ColumnDefinition)window.FindName("SidebarColumn")).Width = new GridLength(270);
            window.UpdateLayout();
            view = Descendants<AllApplicationsView>(window).Single();
            var scroll = (ScrollViewer)view.FindName("ApplicationsScroll");
            Check(Descendants<ScrollViewer>(view).Count() == 1 && scroll.ScrollableWidth == 0 && scroll.ScrollableHeight > 0, "Library retains one vertical scroller and narrow Wrap layout");
            scroll.ScrollToEnd(); window.UpdateLayout();
            Check(scroll.VerticalOffset > 0, "Library scroller reaches final applications");
            SaveWindow(window, "all-small.png");
            await vm.FlushAsync();
            await WaitForAsync(() => ReadProbes(output).Length == 1);
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
        await VerifyLargeAllApplicationsAsync(window);
    }
    private static async Task VerifyLargeAllApplicationsAsync(MainWindow window)
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
        var initialHome = Descendants<AllApplicationsView>(window).Single();
        Check(AllCardViews(window).Length == 40 && !initialHome.IsFullyRendered, "Large Home creates only 40 initial All cards");
        vm.QuickLauncher.Open();
        vm.QuickLauncher.SearchQuery = "应用 299";
        Check(vm.QuickLauncher.Results.Any(x => x.Application.Id == records[299].Id), "Quick Launcher finds not-yet-rendered application");
        vm.QuickLauncher.Close();
        var oldItems = (ItemsControl)initialHome.FindName("AllApplications");
        vm.SelectWorkspace(WorkspaceMode.Settings);
        window.UpdateLayout();
        int oldCount = oldItems.Items.Count;
        await Task.Delay(100);
        Check(!initialHome.IsLoaded && oldItems.Items.Count == oldCount, "Leaving Home cancels pending card batches");
        vm.SelectWorkspace(WorkspaceMode.AllApplications);
        window.UpdateLayout();
        await WaitForAsync(() => Descendants<AllApplicationsView>(window).Single().IsFullyRendered);
        window.UpdateLayout();
        timer.Stop();
        var home = Descendants<AllApplicationsView>(window).Single();
        var scroll = (ScrollViewer)home.FindName("ApplicationsScroll");
        Check(Descendants<ApplicationCardView>(window).Count() == 300 && AllCardViews(window).Length == 300, "300 applications render exactly once without dashboard projections");
        scroll.ScrollToEnd();
        window.UpdateLayout();
        Check(scroll.VerticalOffset > 0 && scroll.ScrollableWidth == 0, "300-card Home scrolls vertically without horizontal overflow");
        var cardsToDispose = vm.Applications.ToArray();
        var removed = vm.Applications[0];
        vm.Applications.RemoveAt(0);
        Check(!vm.Applications.Contains(removed), "Source removal updates all projections and subscriptions");
        vm.Applications.Move(1, 0);
        Check(ReferenceEquals(((ItemsControl)home.FindName("AllApplications")).Items[0], vm.Applications[0]), "Source ordering changes propagate without cloning");
        vm.Applications.Clear();
        window.UpdateLayout();
        Check(vm.IsEmpty && !vm.ShowPageTitle && store.Loads == 1, "Source reset hides sections without any extra database read");
        Check(Descendants<TextBlock>(window).Single(x => AutomationProperties.GetAutomationId(x) == "DropHint").IsVisible, "Reset to zero restores visible Home drop empty state");
        Console.WriteLine($"300-card Home initial layout: {timer.ElapsedMilliseconds} ms");
        foreach (var card in cardsToDispose) card.Dispose();
    }

}
