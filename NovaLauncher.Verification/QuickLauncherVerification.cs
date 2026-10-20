using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using NovaLauncher.App.Models;
using NovaLauncher.App.Persistence;
using NovaLauncher.App.Services;
using NovaLauncher.App.ViewModels;
using NovaLauncher.App.Views;

namespace NovaLauncher.Verification;

internal static partial class Program
{
    private static async Task QuickShortcutAsync(MainWindow window)
    {
        window.Activate();
        LaunchNative.SetForegroundWindow(new WindowInteropHelper(window).Handle);
        await Task.Delay(60);
        Check(window.IsActive, "Keyboard target is the active test window");
        LaunchNative.keybd_event(0x11, 0, 0, UIntPtr.Zero);
        try { await Task.Delay(60); ProfileKey(0x4B); await Task.Delay(150); }
        finally { LaunchNative.keybd_event(0x11, 0, 2, UIntPtr.Zero); }
    }
    private static void PreviewQuickKey(UIElement element, Key key) => element.RaiseEvent(new KeyEventArgs(
        Keyboard.PrimaryDevice, PresentationSource.FromVisual(element), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });

    private static async Task RunQuickLauncherAsync(MainWindow window)
    {
        var paths = new ApplicationDataPaths(Path.Combine(_fixtures, "quick"));
        var cache = new ApplicationIconCache(paths, new ApplicationIconService());
        var management = new ApplicationManagementService(new ApplicationRepository(new LauncherDbContextFactory(paths), paths), cache, cache.CustomIcons);
        using var content = new ContentViewModel(new ApplicationImportService(new ShortcutResolver(), new ApplicationIconService()),
            new ApplicationLaunchService(), CreatePersistence(paths), new ToastService(), CategoryStore(paths), new LocationProbe(), management, new IconPickerStub());
        await content.InitializeAsync();
        var settings = SettingsService(paths); await settings.LoadAsync();
        string backdrop = BackgroundFixture(_fixtures, "quick-background.png");
        var appearance = new AppearanceService(settings, new BackgroundImageStore(paths), new WallpaperStub(backdrop), content.Toast);
        await appearance.InitializeAsync();
        var shell = new MainWindowViewModel(content, settings: new SettingsViewModel(settings, new SettingsDirectoryStub(), paths, content.Toast,
            new AppearanceViewModel(appearance, new BackgroundPicker(), content.Toast)));
        window.DataContext = shell; window.Topmost = true; window.Activate(); window.UpdateLayout();
        var overlay = (QuickLauncherOverlay)window.FindName("QuickOverlay");
        var box = (TextBox)overlay.FindName("QueryBox");
        var list = (ListBox)overlay.FindName("ResultList");
        var panel = (Border)overlay.FindName("PanelSurface");
        var quick = content.QuickLauncher;
        foreach (string name in new[] { "QQ", "QQ音乐", "腾讯QQ", "Visual Studio", "微信", "工具2026", "QQ浏览器", "QQ邮箱", "QQ游戏", "QQ阅读", "QQ影音", "QQ办公" })
        {
            await content.ImportAsync([CopyProbe("Quick " + content.Applications.Count)]);
            var card = content.Applications.Last(); card.RenameDraft = name; await card.RenameCommand.ExecuteAsync(null);
        }
        var trigger = Descendants<Button>(window).Single(x => System.Windows.Automation.AutomationProperties.GetAutomationId(x) == "QuickSearchTrigger");
        trigger.Focus();
        window.PreviewKeyDown += (_, e) => Console.WriteLine($"Keyboard diagnostic: key={e.Key} modifiers={Keyboard.Modifiers} focus={Keyboard.FocusedElement?.GetType().Name}");
        await QuickShortcutAsync(window);
        await WaitForAsync(() => quick.IsOpen && box.IsKeyboardFocused);
        Check(quick.IsOpen && box.IsKeyboardFocused, "Ctrl+K opens and focuses overlay");
        Check(quick.IsEmpty && quick.EmptyHint == "输入应用名称开始搜索", "Empty history/favorites gives light prompt");
        window.UpdateLayout();
        Check(Math.Abs(panel.TranslatePoint(new Point(panel.ActualWidth / 2, 0), overlay).X - overlay.ActualWidth / 2) < 2, "Overlay horizontally centered");
        Check(content.Mode == WorkspaceMode.Home && content.IsHomeDashboard, "Opening never replaces Home");
        quick.Close(); await Task.Delay(60);
        Check(trigger.IsKeyboardFocused, "Closing restores original focus");
        await ClickFavoriteElementAsync(window, trigger); await WaitForAsync(() => quick.IsOpen && box.IsKeyboardFocused);
        Check(quick.IsOpen && box.IsKeyboardFocused, "Mouse trigger opens same overlay");
        quick.Close();
        await content.Applications[0].ToggleFavoriteCommand.ExecuteAsync(null);
        quick.Open(); window.UpdateLayout();
        Check(quick.Results.SequenceEqual([content.Applications[0]]) && quick.Heading == "收藏应用", "Empty query falls back to favorites");
        quick.Close();
        string output = Path.Combine(_fixtures, "quick-launch"); Directory.CreateDirectory(output);
        string? oldOutput = Environment.GetEnvironmentVariable("NOVA_LAUNCH_TEST_OUTPUT");
        Environment.SetEnvironmentVariable("NOVA_LAUNCH_TEST_OUTPUT", output);
        try
        {
            await content.Applications[1].LaunchCommand.ExecuteAsync(null);
            quick.Open(); await Task.Delay(180); window.UpdateLayout();
            Check(quick.Results.SequenceEqual([content.Applications[1]]) && quick.Heading == "最近使用", "Recent overrides favorites for empty query");
            SaveWindow(window, "quick-empty-query.png");
            box.Text = "  qq  ";
            Check(quick.Results.Count == 8 && quick.Results[0].DisplayName == "QQ" && quick.Results.Skip(1).All(x => x.DisplayName.StartsWith("QQ")), "Top8 exact then prefix ranking, trimmed insensitive match");
            Check(quick.SelectedIndex == 0, "First result selected by default");
            await QuickShortcutAsync(window);
            Check(box.SelectedText == box.Text && quick.IsOpen, "Repeated Ctrl+K selects all without closing");
            box.Text = "stu"; Check(quick.Results.Single().DisplayName == "Visual Studio", "English Contains matching");
            box.Text = "音乐"; Check(quick.Results.Single().DisplayName == "QQ音乐", "Chinese Contains matching");
            box.Text = "2026"; Check(quick.Results.Single().DisplayName == "工具2026", "Numeric matching");
            box.Text = "qq"; await Task.Delay(180); window.UpdateLayout(); SaveWindow(window, "quick-results.png");
            ProfileKey(0x28); await Task.Delay(80); Check(quick.SelectedIndex == 1, "Down selects next without changing query");
            ProfileKey(0x26); await Task.Delay(80); Check(quick.SelectedIndex == 0, "Up selects previous");
            for (int i = 0; i < 12; i++) PreviewQuickKey(box, Key.Down);
            Check(quick.SelectedIndex == 7 && quick.SearchQuery == "qq", "Down clamps at last result");
            box.Text = "微信"; Check(quick.Results.Count == 1 && quick.SelectedIndex == 0, "Query change resets old selection safely");
            box.Text = "不存在"; Check(quick.Results.Count == 0 && quick.SelectedIndex == -1, "No results clears selection");
            await quick.LaunchSelectedAsync(); Check(quick.IsOpen, "Enter with no results does nothing");
            box.Text = "qq"; PreviewQuickKey(box, Key.Down); await Task.Delay(180); window.UpdateLayout(); SaveWindow(window, "quick-keyboard-selection.png");
            var selected = quick.Results[quick.SelectedIndex]; long count = selected.LaunchCount;
            ProfileKey(0x0D); await WaitForAsync(() => !quick.IsOpen);
            Check(selected.LaunchCount == count + 1 && selected.LastLaunchTime.HasValue && content.Home.RecentApplications.Contains(selected), "Enter launches selected via shared command and closes on success");
            Check(quick.SearchQuery == "" && content.Mode == WorkspaceMode.Home, "Successful launch clears query and keeps page");
            foreach (var item in new NavigationItem[] { content.CategoryManager.Categories[0], shell.Sidebar.FooterItems[0], shell.Sidebar.FooterItems[1], shell.Sidebar.SettingsItem })
            {
                shell.Sidebar.SelectedItem = item; var mode = content.Mode; var category = content.SelectedCategory;
                await QuickShortcutAsync(window);
                box.Text = "微信";
                Check(quick.IsOpen && content.Mode == mode && content.SelectedCategory == category && shell.Sidebar.SelectedItem == item, $"Overlay preserves {item.Name}");
                ProfileKey(0x1B); await WaitForAsync(() => !quick.IsOpen);
                Check(content.Mode == mode && quick.SearchQuery == "", $"Esc returns unchanged {item.Name}");
            }
            await QuickShortcutAsync(window);
            box.Text = "微信"; window.UpdateLayout();
            var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            var point = row.PointToScreen(new Point(row.ActualWidth - 30, row.ActualHeight / 2));
            Native.SetCursorPos((int)point.X, (int)point.Y); await Task.Delay(100);
            Check(row.IsMouseOver, "Whole result row has hover surface");
            var mouseTarget = quick.Results[0]; long mouseCount = mouseTarget.LaunchCount;
            await ClickFavoriteElementAsync(window, row); await WaitForAsync(() => !quick.IsOpen);
            Check(mouseTarget.LaunchCount == mouseCount + 1 && content.Mode == WorkspaceMode.Settings, "Single mouse click launches and preserves Settings");
            await QuickShortcutAsync(window);
            var outside = overlay.PointToScreen(new Point(20, 20)); Native.SetCursorPos((int)outside.X, (int)outside.Y);
            Check(LaunchNative.WindowFromPoint(new((int)outside.X, (int)outside.Y)) == new WindowInteropHelper(window).Handle, "Scrim click target verified");
            LaunchNative.mouse_event(2,0,0,0,UIntPtr.Zero); LaunchNative.mouse_event(4,0,0,0,UIntPtr.Zero);
            await WaitForAsync(() => !quick.IsOpen); Check(content.Mode == WorkspaceMode.Settings, "Outside click closes without navigation");
            await QuickShortcutAsync(window);
            box.Text = "QQ"; var first = content.Applications[0];
            first.RenameDraft = "其他名称"; await first.RenameCommand.ExecuteAsync(null);
            Check(!quick.Results.Contains(first), "Rename updates open results immediately");
            var removed = quick.Results[0]; await removed.RemoveCommand.ExecuteAsync(null);
            Check(!quick.Results.Contains(removed) && quick.SelectedIndex < quick.Results.Count, "Removal updates Top8 and selection safely");
            await VerifyQuickImeAsync(window, overlay, box, quick);
            box.Text = "qq";
            await appearance.ImportAsync(backdrop); appearance.PreviewParameters(0.25, 0, BackgroundStretch.UniformToFill); await appearance.FlushAsync();
            await Task.Delay(180); window.UpdateLayout(); SaveWindow(window, "quick-custom-image.png");
            Check(appearance.HasImage && panel.Background.Opacity == 1, "Opaque quick surface over low-overlay custom background");
            await appearance.SetModeAsync(NovaLauncher.App.Models.BackgroundMode.DesktopWallpaper);
            window.UpdateLayout(); SaveWindow(window, "quick-desktop-wallpaper.png");
            Check(quick.IsOpen && appearance.HasImage, "Desktop wallpaper preserves overlay");
            window.Width = 800; window.Height = 560; window.UpdateLayout();
            Check(panel.TranslatePoint(new Point(0,panel.ActualHeight),overlay).Y <= overlay.ActualHeight, "Small window keeps panel within bounds");
            SaveWindow(window, "quick-small.png");
            quick.Close(); await content.FlushAsync();
            await WaitForAsync(() => ReadProbes(output).Length == 3);
        }
        finally { Environment.SetEnvironmentVariable("NOVA_LAUNCH_TEST_OUTPUT", oldOutput); }
        await VerifyQuickPerformanceAsync(window);
        await VerifyQuickLaunchBoundariesAsync();
    }

    private static async Task VerifyQuickImeAsync(MainWindow window, QuickLauncherOverlay overlay, TextBox box, QuickLauncherViewModel quick)
    {
        box.Text = "qq"; box.Focus();
        long before = quick.Results.Sum(x => x.LaunchCount);
        var composition = new TextComposition(InputManager.Current, box, "wei");
        box.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = TextCompositionManager.PreviewTextInputStartEvent });
        int index = quick.SelectedIndex;
        foreach (Key key in new[] { Key.Enter, Key.Space, Key.Down, Key.Up, Key.Escape })
        {
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(box),0,key) { RoutedEvent=Keyboard.PreviewKeyDownEvent };
            box.RaiseEvent(args);
            Check(!args.Handled, "IME composition leaves " + key + " to text input");
        }
        Check(quick.IsOpen && quick.SelectedIndex == index && quick.Results.Sum(x=>x.LaunchCount)==before, "Composition never launches or navigates");
        box.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = TextCompositionManager.PreviewTextInputEvent });
        Check(!overlay.IsComposing, "Completed composition releases command guard");

        var languages = InputLanguageManager.Current.AvailableInputLanguages.Cast<CultureInfo>().ToArray();
        Console.WriteLine("Installed input languages: " + string.Join(", ",languages.Select(x=>x.Name)));
        var chinese = languages.FirstOrDefault(x => x.Name == "zh-CN");
        if (chinese is null) { Console.WriteLine("Native Chinese IME unavailable; composition event contract tested only."); return; }
        var original = InputLanguageManager.Current.CurrentInputLanguage;
        try
        {
            quick.Close(); quick.Open(); await Task.Delay(150); box.Focus();
            InputLanguageManager.Current.CurrentInputLanguage = chinese;
            InputMethod.Current.ImeState = InputMethodState.On;
            await Task.Delay(400);
            foreach (char c in "weixin") { ProfileKey((byte)char.ToUpperInvariant(c)); await Task.Delay(80); }
            Console.WriteLine($"Native IME preedit: composing={overlay.IsComposing}, text={box.Text}");
            SaveWindow(window, "quick-ime-composition.png");
            ProfileKey(0x20); await Task.Delay(250);
            Console.WriteLine("Native IME committed: " + box.Text);
            Check(quick.IsOpen && box.Text.Any(c => c >= 0x4e00 && c <= 0x9fff), "Native Chinese IME commits Chinese text without launching");
            Check(!overlay.IsComposing, "Native IME commit releases composition guard");
            SaveWindow(window, "quick-ime-committed.png");
            box.SelectAll();
            foreach (char c in "weixin") { ProfileKey((byte)char.ToUpperInvariant(c)); await Task.Delay(80); }
            ProfileKey(0x0D); await Task.Delay(180);
            Check(quick.IsOpen && !overlay.IsComposing, "Native IME Enter confirms preedit without launching or closing");

        }
        finally { InputLanguageManager.Current.CurrentInputLanguage = original; }
    }

    private static async Task VerifyQuickLaunchBoundariesAsync()
    {
        var app=new LauncherApplication(Guid.NewGuid(),"QQ", @"C:\Quick\QQ.exe", @"C:\Quick\QQ.exe",null,ApplicationSourceType.Executable);
        var launcher=new ControlledLauncher();
        var store=new RecentMemoryStore([app]);
        using var content=new ContentViewModel(new ApplicationImportService(new ShortcutResolver(),new ApplicationIconService()),launcher,store,new ToastService());
        await content.InitializeAsync();
        var quick=content.QuickLauncher;
        quick.Open();quick.SearchQuery="qq";
        launcher.Reset(new ApplicationLaunchResult(false,"启动失败测试"));
        await quick.LaunchSelectedAsync();
        Check(quick.IsOpen && quick.SearchQuery=="qq" && quick.Error=="启动失败测试" && content.Applications[0].LaunchCount==0,"Failed launch preserves query, selection, error and counts");
        quick.Close();
        var delayed=new ControlledLauncher();
        using var other=new ContentViewModel(new ApplicationImportService(new ShortcutResolver(),new ApplicationIconService()),delayed,new RecentMemoryStore([app]),new ToastService());
        await other.InitializeAsync();
        var next=other.QuickLauncher;
        next.Open();next.SearchQuery="qq";
        var pending=next.LaunchSelectedAsync();
        next.Close();next.Open();next.SearchQuery="QQ";
        delayed.Complete(new ApplicationLaunchResult(true));
        await pending;
        Check(next.IsOpen && next.SearchQuery=="QQ","Old launch completion does not close a newly opened session");
        next.Close();
    }
    private static async Task VerifyQuickPerformanceAsync(MainWindow window)
    {
        var records = Enumerable.Range(0,300).Select(i=>new LauncherApplication(Guid.NewGuid(),$"应用 App {i:D3}",
            $@"C:\Quick\{i}.exe",$@"C:\Quick\{i}.exe",null,ApplicationSourceType.Executable)
            { LastLaunchTime = i<10 ? DateTimeOffset.Now.AddMinutes(-i) : null }).ToArray();
        var store = new RecentMemoryStore(records);
        using var content = new ContentViewModel(new ApplicationImportService(new ShortcutResolver(),new ApplicationIconService()),
            new ApplicationLaunchService(),store,new ToastService());
        await content.InitializeAsync(); window.DataContext=new MainWindowViewModel(content); window.Width=1120;window.Height=720;
        var quick=content.QuickLauncher; quick.Open(); window.UpdateLayout();
        Check(quick.Results.Count==6 && quick.Results[0].Application==records[0],"Empty query caps recent at six, newest first");
        var model=new List<double>();var layout=new List<double>();var calculation=new List<double>();
        using var search=new ApplicationSearchService(content.Applications);
        foreach(var query in new[]{"App","App 2","App 29","App 299","App","不存在"})
        {
            search.Query=query;
            var timer=Stopwatch.StartNew();
            var matches=content.Applications.Where(search.Matches).ToArray();
            Array.Sort(matches,(a,b)=>search.Compare(a,b));
            _=matches.Take(8).ToArray();
            calculation.Add(timer.Elapsed.TotalMilliseconds);
            timer.Restart();quick.SearchQuery=query;model.Add(timer.Elapsed.TotalMilliseconds);
            timer.Restart();window.UpdateLayout();await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Render);layout.Add(timer.Elapsed.TotalMilliseconds);
            Check(quick.Results.Count<=8,"300-app result cap");
        }
        Console.WriteLine("300-app pure match/rank ms: "+string.Join(", ",calculation.Select(x=>x.ToString("F3"))));
        Console.WriteLine("300-app query/projection/bindings ms: "+string.Join(", ",model.Select(x=>x.ToString("F2"))));
        Console.WriteLine("300-app overlay layout/render ms: "+string.Join(", ",layout.Select(x=>x.ToString("F2"))));
        Check(store.Loads==1 && content.Applications.Select(c=>c.Application).SequenceEqual(records),"Search never reloads storage or clones card models");
        quick.SearchQuery="App";window.UpdateLayout();
        var list=Descendants<QuickLauncherOverlay>(window).Single().FindName("ResultList") as ListBox;
        Check(Descendants<ListBoxItem>(list!).Count()<=8,"At most eight result visual rows");
        quick.Close();
    }
}
