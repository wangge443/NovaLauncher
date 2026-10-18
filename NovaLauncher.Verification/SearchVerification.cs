using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using NovaLauncher.App.Models;
using NovaLauncher.App.Persistence;
using NovaLauncher.App.Services;
using NovaLauncher.App.ViewModels;
using NovaLauncher.App.Views;

namespace NovaLauncher.Verification;

internal static partial class Program
{
    private static async Task SearchShortcutAsync(MainWindow window, TextBox box)
    {
        Check(window.IsActive, "Keyboard input restricted to active verification window");
        LaunchNative.keybd_event(0x11, 0, 0, UIntPtr.Zero);
        try { ProfileKey(0x4B); await Task.Delay(80); }
        finally { LaunchNative.keybd_event(0x11, 0, 2, UIntPtr.Zero); }
        await WaitForAsync(() => box.IsKeyboardFocused);
    }

    private static async Task RunSearchAsync(MainWindow window)
    {
        var paths = new ApplicationDataPaths(Path.Combine(_fixtures, "search"));
        var cache = new ApplicationIconCache(paths, new ApplicationIconService());
        var management = new ApplicationManagementService(new ApplicationRepository(new LauncherDbContextFactory(paths), paths), cache, cache.CustomIcons);
        using var vm = new ContentViewModel(new ApplicationImportService(new ShortcutResolver(), new ApplicationIconService()),
            new ApplicationLaunchService(), CreatePersistence(paths), new ToastService(), CategoryStore(paths), new LocationProbe(), management, new IconPickerStub());
        await vm.InitializeAsync();
        var settings = SettingsService(paths); await settings.LoadAsync();
        string backdrop = BackgroundFixture(_fixtures, "search-background.png");
        var appearance = new AppearanceService(settings, new BackgroundImageStore(paths), new WallpaperStub(backdrop), vm.Toast);
        await appearance.InitializeAsync();
        var shell = new MainWindowViewModel(vm, settings: new SettingsViewModel(settings, new SettingsDirectoryStub(), paths, vm.Toast,
            new AppearanceViewModel(appearance, new BackgroundPicker(), vm.Toast)));
        window.DataContext = shell; window.Topmost = true; window.Activate(); window.UpdateLayout();
        var view = Descendants<ContentView>(window).Single();
        var box = (TextBox)view.FindName("SearchBox");
        foreach (string name in new[] { "QQ", "QQ音乐", "腾讯QQ", "Visual Studio", "微信", "工具2026" })
        {
            await vm.ImportAsync([CopyProbe("Search " + vm.Applications.Count)]);
            var card = vm.Applications.Last(); card.RenameDraft = name; await card.RenameCommand.ExecuteAsync(null);
        }
        window.UpdateLayout();
        Check(box.IsVisible && box.ActualHeight <= 42, "Compact search visible on Home");
        var quiet = window.PointToScreen(new Point(700, 70)); Native.SetCursorPos((int)quiet.X, (int)quiet.Y); await Task.Delay(220);
        SaveWindow(window, "home-search.png");
        await SearchShortcutAsync(window, box);
        box.Text = "  qq  ";
        Check(vm.IsSearching && RecentCards(vm).Select(x => x.DisplayName).SequenceEqual(new[] { "QQ", "QQ音乐", "腾讯QQ" }), "Trim, case insensitive, exact-prefix-contains ranking");
        await SearchShortcutAsync(window, box);
        Check(box.SelectedText == box.Text, "Repeated Ctrl+K selects all query text");
        box.Text = "stu"; Check(RecentCards(vm).Single().DisplayName == "Visual Studio", "English Contains");
        box.Text = "微信"; Check(RecentCards(vm).Single().DisplayName == "微信", "Chinese matching");
        box.Text = "2026"; Check(RecentCards(vm).Single().DisplayName == "工具2026", "Digit matching");
        box.Text = "qq"; window.UpdateLayout(); SaveWindow(window, "search-results.png");
        foreach (var item in new NavigationItem[] { shell.Sidebar.Items[0], vm.CategoryManager.Categories[0], shell.Sidebar.FooterItems[0], shell.Sidebar.FooterItems[1] })
        {
            vm.SearchQuery = ""; shell.Sidebar.SelectedItem = item;
            box.Text = "微信";
            Check(vm.IsSearching && RecentCards(vm).Length == 1 && shell.Sidebar.SelectedItem == item, $"Global search from {item.Name} retains Sidebar");
            SendEditorKey(box, Key.Escape);
            Check(vm.Mode == (item is CategoryViewModel ? WorkspaceMode.Category : item.Mode) && (item is not CategoryViewModel category || vm.SelectedCategory == category), $"Escape restores {item.Name} and category");
        }
        shell.Sidebar.SelectedItem = shell.Sidebar.FooterItems[0]; shell.Sidebar.OpenSettings(); window.UpdateLayout();
        Check(!box.IsVisible, "Settings hides search");
        await SearchShortcutAsync(window, box);
        Check(vm.Mode == WorkspaceMode.Favorites && box.IsVisible, "Ctrl+K in Settings restores last application page");
        box.Text = "qq"; window.UpdateLayout();
        var clear = Descendants<Button>(view).Single(x => System.Windows.Automation.AutomationProperties.GetAutomationId(x) == "ClearSearch");
        clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(vm.Mode == WorkspaceMode.Favorites && box.Text == "", "Clear restores origin");
        SendEditorKey(box, Key.Escape);
        Check(!box.IsKeyboardFocused && window.IsVisible, "Empty Escape releases focus without closing");
        box.Text = "   "; Check(!vm.IsSearching, "Whitespace query stays out of Search");
        box.Text = "zzzz不存在"; window.UpdateLayout();
        Check(vm.IsEmpty && vm.EmptyTitle == "未找到应用" && !vm.EmptyHint.Contains("拖"), "Dedicated no-result state");
        SaveWindow(window, "search-empty.png");
        box.Text = "qq"; view.FocusSearch(); window.UpdateLayout();
        var first = RecentCards(vm)[0];
        string output = Path.Combine(_fixtures, "search-launch"); Directory.CreateDirectory(output);
        string? oldOutput = Environment.GetEnvironmentVariable("NOVA_LAUNCH_TEST_OUTPUT");
        Environment.SetEnvironmentVariable("NOVA_LAUNCH_TEST_OUTPUT", output);
        try
        {
            SendEditorKey(box, Key.Enter);
            await WaitForAsync(() => first.LaunchCount == 1 && !first.IsLaunching);
            await WaitForAsync(() => ReadProbes(output).Length == 1);
            Check(first.LastLaunchTime.HasValue && vm.SearchQuery == "qq" && vm.IsSearching, "Enter uses real launch pipeline and preserves query/statistics");
        }
        finally { Environment.SetEnvironmentVariable("NOVA_LAUNCH_TEST_OUTPUT", oldOutput); }
        box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        for (int i = 0; i < 4 && Keyboard.FocusedElement is not Button { DataContext: ApplicationCardViewModel }; i++)
            (Keyboard.FocusedElement as UIElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        Check(Keyboard.FocusedElement is Button { DataContext: ApplicationCardViewModel }, "Tab traversal reaches application card");
        window.UpdateLayout();
        var cardView = AllCardViews(window).First(x => x.DataContext == first);
        await OpenCardMenuAsync(cardView); Check(cardView.ContextMenu.IsOpen, "Search reuses functioning context menu"); cardView.ContextMenu.IsOpen = false;
        await first.ToggleFavoriteCommand.ExecuteAsync(null);
        await first.ChangeCategoryCommand.ExecuteAsync(new(vm.CategoryManager.Categories[0].Id, vm.CategoryManager.Categories[0].Name));
        Check(first.IsFavorite && first.CategoryId.HasValue && RecentCards(vm).Contains(first), "Favorite/category changes retain global result");
        first.RenameDraft = "腾讯QQ 新"; await first.RenameCommand.ExecuteAsync(null);
        Check(RecentCards(vm).Contains(first), "Matching rename remains");
        first.RenameDraft = "其他名称"; await first.RenameCommand.ExecuteAsync(null);
        Check(!RecentCards(vm).Contains(first), "Nonmatching rename immediately disappears");
        var removed = RecentCards(vm)[0]; await removed.RemoveCommand.ExecuteAsync(null);
        Check(!RecentCards(vm).Contains(removed) && File.Exists(removed.TargetPath), "Removal updates results and preserves executable");
        string imported = CopyProbe("Search Import");
        string matchingLink = MakeShortcut("QQ 新应用", imported);
        await DropAsync(window, view, [matchingLink]);
        await WaitForAsync(() => !vm.IsImporting);
        Check(RecentCards(vm).Any(x => x.DisplayName == "QQ 新应用"), "Matching imported application appears immediately");
        await DropAsync(window, view, [CopyProbe("Unrelated")]);
        await WaitForAsync(() => !vm.IsImporting);
        Check(RecentCards(vm).All(x => x.DisplayName.Contains("qq", StringComparison.OrdinalIgnoreCase)), "Nonmatching import remains outside results");
        await appearance.ImportAsync(backdrop); appearance.PreviewParameters(0.3, 0, BackgroundStretch.UniformToFill);
        await appearance.FlushAsync(); Native.SetCursorPos((int)quiet.X, (int)quiet.Y); Keyboard.ClearFocus(); await Task.Delay(220); window.UpdateLayout();
        Check(box.IsVisible && appearance.HasImage, "CustomImage search surface visible"); SaveWindow(window, "search-custom-image.png");
        await appearance.SetModeAsync(NovaLauncher.App.Models.BackgroundMode.DesktopWallpaper); window.UpdateLayout();
        Check(appearance.HasImage && vm.IsSearching, "DesktopWallpaper retains search"); SaveWindow(window, "search-desktop-wallpaper.png");
        window.Width = 800; window.Height = 560; window.UpdateLayout();
        Check(Descendants<ScrollViewer>(view).All(x => x.ScrollableWidth == 0), "Narrow search has no horizontal scrolling");
        SaveWindow(window, "search-small.png");
        await vm.FlushAsync();
        await VerifySearchPerformanceAsync(window);
    }

    private static async Task VerifySearchPerformanceAsync(MainWindow window)
    {
        var records = Enumerable.Range(0, 300).Select(i => new LauncherApplication(Guid.NewGuid(), $"应用 App {i:D3}",
            $@"C:\Search\{i}.exe", $@"C:\Search\{i}.exe", null, ApplicationSourceType.Executable)).ToArray();
        var store = new RecentMemoryStore(records);
        using var vm = new ContentViewModel(new ApplicationImportService(new ShortcutResolver(), new ApplicationIconService()),
            new ApplicationLaunchService(), store, new ToastService());
        await vm.InitializeAsync();
        window.DataContext = new MainWindowViewModel(vm); window.Width = 1120; window.Height = 720;
        vm.SearchQuery = "App"; window.UpdateLayout();
        var times = new List<double>();
        foreach (string query in new[] { "App 2", "App 29", "App 299", "App 29", "App 2", "不存在" })
        {
            var timer = Stopwatch.StartNew(); vm.SearchQuery = query; window.UpdateLayout();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            times.Add(timer.Elapsed.TotalMilliseconds);
        }
        Check(store.Loads == 1 && vm.Applications.Select(c => c.Application).SequenceEqual(records), "300 applications reuse loaded instances without database reads");
        Console.WriteLine("300-app query + layout ms: " + string.Join(", ", times.Select(x => x.ToString("F1"))));
        vm.SearchQuery = "App 299"; Check(RecentCards(vm).Single().Application == records[299], "300-app query result correct");
        vm.SearchQuery = "App 29"; var before = RecentCards(vm); vm.SearchQuery = "App 299"; vm.SearchQuery = "App 29";
        Check(RecentCards(vm).SequenceEqual(before), "Repeated search ordering is stable");
        vm.SearchQuery = "App"; window.UpdateLayout();
        Check(AllCardViews(window).Length < 40, "300 results realize only viewport cards");
        var panel = Descendants<SearchWrapPanel>(window).Single();
        panel.ScrollOwner.ScrollToEnd(); await Task.Delay(100); window.UpdateLayout();
        Console.WriteLine($"Scroll offset={panel.VerticalOffset} extent={panel.ExtentHeight} viewport={panel.ViewportHeight}; last={AllCardViews(window).Length}");
        Check(AllCardViews(window).Any(c => ((ApplicationCardViewModel)c.DataContext).Application == records[299]), "Virtualized scrolling reaches last result");
        panel.ScrollOwner.ScrollToTop(); await Task.Delay(100); window.UpdateLayout();
        ((Button)AllCardViews(window)[0].FindName("LaunchButton")).Focus();

        // Routed KeyDown helpers do not emulate WPF's Preview event; use real Tab for viewport traversal.
        ((Button)AllCardViews(window)[0].FindName("LaunchButton")).Focus();
        for (int i = 0; i < 35; i++) { ProfileKey(0x09); await Task.Delay(10); }
        Check(Keyboard.FocusedElement is Button { DataContext: ApplicationCardViewModel selected } && selected.Application == records[35], "Tab crosses virtualized viewport boundaries");
        window.Width = 800; window.UpdateLayout();
        Check(panel.ExtentHeight > panel.ViewportHeight && panel.ViewportWidth < 600, "Virtualized wrap recalculates narrow viewport");
        vm.SearchQuery = "App 299"; window.UpdateLayout();
        Check(AllCardViews(window).Length == 1 && panel.VerticalOffset == 0, "Narrowing scrolled search restores visible result at top");
    }
}
