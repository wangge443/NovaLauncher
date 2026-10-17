using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using NovaLauncher.App.Models;
using NovaLauncher.App.Persistence;
using NovaLauncher.App.Services;
using NovaLauncher.App.ViewModels;
using NovaLauncher.App.Views;

namespace NovaLauncher.Verification;

internal static partial class Program
{
    private static async Task RunPolishAsync(MainWindow window)
    {
        // Isolated presentation fixtures: read system icons, never launch these applications.
        var importer = new ApplicationImportService(new ShortcutResolver(), new ApplicationIconService());
        string[] sources = [Path.Combine(Environment.SystemDirectory, "notepad.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
            Path.Combine(Environment.SystemDirectory, "cmd.exe"), Path.Combine(Environment.SystemDirectory, "Taskmgr.exe")];
        string[] names = ["记事本", "文件资源管理器", "终端", "任务管理器", "工作笔记", "项目文件", "开发终端", "系统工具"];
        var records = new List<LauncherApplication>();
        for (int i = 0; i < names.Length; i++)
        {
            var result = await importer.ImportAsync(sources[i % sources.Length], default);
            records.Add(result.Application! with { Id = Guid.NewGuid(), DisplayName = names[i],
                ExecutablePath = Path.Combine(_fixtures, $"visual-{i}.exe"), SourcePath = Path.Combine(_fixtures, $"visual-{i}.exe"),
                LastLaunchTime = i < 4 ? DateTimeOffset.Now.AddMinutes(-i * 12) : null,
                LaunchCount = i < 4 ? 8 - i : 0, IsFavorite = i is 0 or 2 or 4 });
        }
        var paths = new ApplicationDataPaths(Path.Combine(_fixtures, "polish"));
        await CreatePersistence(paths).LoadAsync(default);
        using var vm = new ContentViewModel(importer, new ApplicationLaunchService(), new RecentMemoryStore(records),
            new ToastService(), CategoryStore(paths));
        await vm.InitializeAsync();
        var shell = new MainWindowViewModel(vm);
        window.DataContext = shell;
        window.Width = 1120; window.Height = 760;
        window.Topmost = true; window.Activate();
        window.UpdateLayout();
        var caption = window.PointToScreen(new Point(450, 22));
        Native.SetCursorPos((int)caption.X, (int)caption.Y);
        await Task.Delay(250);
        SaveWindow(window, "home.png");
        var card = AllCardViews(window)[0];
        var size = card.RenderSize;
        Check(Descendants<ApplicationCardView>(window).All(x => x.RenderSize == size), "All Home sections use identical card dimensions");
        var home = Descendants<HomeView>(window).Single();
        Check(Descendants<Button>(home).Where(x => System.Windows.Automation.AutomationProperties.GetAutomationId(x).StartsWith("HomeShow")).All(x => x.ActualHeight >= 32 && x.ActualWidth >= 88), "View-all actions have usable hit areas");
        var scroll = (ScrollViewer)home.FindName("HomeScroll");
        var bar = Descendants<ScrollBar>(scroll).Single(x => x.Orientation == Orientation.Vertical);
        Check(bar.ActualWidth == 12 && Descendants<Track>(bar).Any(), $"Slim scrollbar retains native Track (width={bar.ActualWidth}, visible={bar.IsVisible}, extent={scroll.ScrollableHeight})");
        ScrollBar.PageDownCommand.Execute(null, bar);
        await Task.Delay(150);
        Check(scroll.VerticalOffset > 0, "Native scrollbar page command scrolls Home");
        var range = (IRangeValueProvider)new ScrollBarAutomationPeer(bar).GetPattern(PatternInterface.RangeValue);
        range.SetValue(range.Maximum);
        await Task.Delay(150);
        Check(Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight) < 1, "Scrollbar automation reaches end");
        scroll.ScrollToTop(); window.UpdateLayout();
        card = Descendants<ApplicationCardView>((ItemsControl)home.FindName("RecentApplications")).First();
        var before = card.RenderSize;
        var point = card.PointToScreen(new Point(card.ActualWidth / 2, card.ActualHeight / 2));
        Native.SetCursorPos((int)point.X, (int)point.Y);
        await WaitForAsync(() => card.IsMouseOver);
        await Task.Delay(250);
        Check(card.RenderSize == before && ((FrameworkElement)card.FindName("DetailsContent")).Opacity > 0.99,
            "Actual card hover reveals details without layout movement");
        SaveWindow(window, "card-hover.png");
        Native.SetCursorPos((int)caption.X, (int)caption.Y);
        await Task.Delay(250);
        Check(((FrameworkElement)card.FindName("NormalContent")).Opacity > 0.99, "Pointer leave restores quiet card");

        var category = vm.CategoryManager.Categories.First();
        await vm.Applications[0].ChangeCategoryCommand.ExecuteAsync(new(category.Id, category.Name));
        await vm.Applications[1].ChangeCategoryCommand.ExecuteAsync(new(category.Id, category.Name));
        foreach (var mode in new[] { WorkspaceMode.Category, WorkspaceMode.Favorites, WorkspaceMode.Recent })
        {
            shell.Sidebar.SelectedItem = mode == WorkspaceMode.Category ? category : shell.Sidebar.FooterItems.Single(x => x.Mode == mode);
            window.UpdateLayout(); await Task.Delay(180);
            Check(AllCardViews(window).All(x => x.RenderSize == size), $"{mode} shares identical card size");
            SaveWindow(window, mode.ToString().ToLowerInvariant() + ".png");
        }
        var navigation = Descendants<ListBoxItem>(window).First(x => ReferenceEquals(x.DataContext, category));
        point = navigation.PointToScreen(new Point(70, 22));
        Native.SetCursorPos((int)point.X, (int)point.Y);
        await WaitForAsync(() => navigation.IsMouseOver); await Task.Delay(250);
        SaveWindow(window, "sidebar-hover.png");
        Check(Descendants<ListBoxItem>(Descendants<SidebarView>(window).Single()).All(x => x.ActualHeight == 44),
            "Sidebar navigation retains uniform row height");
        Native.SetCursorPos((int)caption.X, (int)caption.Y);
        shell.Sidebar.SelectedItem = shell.Sidebar.Items[0];
        window.UpdateLayout();
        home = Descendants<HomeView>(window).Single();
        scroll = (ScrollViewer)home.FindName("HomeScroll");
        vm.Toast.Show("该应用已添加"); await Task.Delay(250);
        SaveWindow(window, "toast.png");
        var toast = Descendants<ToastView>(window).Single();
        Check(!toast.IsHitTestVisible && !toast.Focusable, "Toast does not intercept card input or focus");
        await Task.Delay(2200);
        Check(!vm.Toast.IsOpen, "Toast expires after about two seconds");
        window.Width = 800; window.Height = 560;
        var column = (ColumnDefinition)window.FindName("SidebarColumn");
        foreach (double width in new[] { 220d, 320d })
        {
            column.Width = new GridLength(width); window.UpdateLayout();
            Check(scroll.ScrollableWidth == 0, "Minimum window and resized Sidebar have no horizontal overflow");
            SaveWindow(window, width == 220 ? "sidebar-narrow.png" : "sidebar-wide.png");
        }
        window.WindowState = WindowState.Maximized; await Task.Delay(300); window.UpdateLayout();
        SaveWindow(window, "maximized.png");
        Check(scroll.ScrollableWidth == 0, "Maximized Home has no horizontal overflow");
        window.WindowState = WindowState.Normal; window.Width = 1120; window.Height = 760; column.Width = new GridLength(280);
        foreach (var application in vm.Applications.ToArray()) application.Dispose();
        vm.Applications.Clear();
        foreach (var mode in new[] { WorkspaceMode.Home, WorkspaceMode.Favorites, WorkspaceMode.Recent })
        {
            shell.Sidebar.SelectedItem = mode == WorkspaceMode.Home ? shell.Sidebar.Items[0] : shell.Sidebar.FooterItems.Single(x => x.Mode == mode);
            window.UpdateLayout(); await Task.Delay(150);
            Check(vm.IsEmpty, $"{mode} empty state remains available");
            SaveWindow(window, "empty-" + mode.ToString().ToLowerInvariant() + ".png");
        }
    }
}
