using System.Collections.ObjectModel;
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
    private static async Task RunPinyinAsync(MainWindow window)
    {
        long coldRetained = GC.GetTotalMemory(true);
        long coldBytes = GC.GetTotalAllocatedBytes(true);
        var timer = Stopwatch.StartNew();
        var first = ApplicationSearchEntry.Create(Guid.NewGuid(), "微信");
        Console.WriteLine($"Pinyin cold initialization: {timer.Elapsed.TotalMilliseconds:F2} ms; allocated {GC.GetTotalAllocatedBytes(true) - coldBytes:N0} bytes");
        Console.WriteLine($"Pinyin cold retained heap delta after GC: {GC.GetTotalMemory(true) - coldRetained:N0} bytes (includes runtime/JIT activity)");
        Check(first.FullPinyin == "weixin" && first.PinyinInitials == "wx", "Offline toneless keys for 微信");
        var mixed = ApplicationSearchEntry.Create(Guid.NewGuid(), "QQ音乐 2026");
        Check(mixed.NormalizedName == "qq音乐2026" && mixed.FullPinyin == "qqyinyue2026" && mixed.PinyinInitials == "qqyy2026", "Mixed ASCII, phrase pinyin, initials and digits preserved");
        var umlaut = ApplicationSearchEntry.Create(Guid.NewGuid(), "吕女");
        Check(umlaut.FullPinyin == "lvnv", "Umlaut syllables use ASCII v without tone");

        var paths = new ApplicationDataPaths(Path.Combine(_fixtures, "pinyin"));
        var cache = new ApplicationIconCache(paths, new ApplicationIconService());
        var management = new ApplicationManagementService(new ApplicationRepository(new LauncherDbContextFactory(paths), paths), cache, cache.CustomIcons);
        using var content = new ContentViewModel(new ApplicationImportService(new ShortcutResolver(), new ApplicationIconService()),
            new ApplicationLaunchService(), CreatePersistence(paths), new ToastService(), CategoryStore(paths), new LocationProbe(), management, new IconPickerStub());
        await content.InitializeAsync();
        string[] names = ["微信", "记事本", "QQ音乐", "网易云音乐", "Visual Studio", "Visual Studio Code", "3DMark", "C++", "工作微信",
            "QQ", "QQ浏览器", ".NET", "微信(测试版)", "B站", "🎮游戏助手", "Visual Studio 2026", "工具-A_B.C", "𠮷🪐"];
        foreach (string name in names)
        {
            await content.ImportAsync([CopyProbe("Pinyin " + content.Applications.Count)]);
            var card = content.Applications.Last();
            card.RenameDraft = name;
            await card.RenameCommand.ExecuteAsync(null);
        }
        window.DataContext = new MainWindowViewModel(content);
        window.Width = 1120; window.Height = 720; window.Topmost = true;
        var quick = content.QuickLauncher;
        quick.Open();
        using var search = new ApplicationSearchService(content.Applications);
        foreach (var (query, target) in new (string, string)[]
        {
            ("微信", "微信"), ("微", "微信"), ("wx", "微信"), ("weixin", "微信"), (" Wei Xin ", "微信"),
            ("记事本", "记事本"), ("记事", "记事本"), ("jsb", "记事本"), ("jishiben", "记事本"),
            ("qq", "QQ音乐"), ("qqyy", "QQ音乐"), ("QQYINYUE", "QQ音乐"), ("音乐", "QQ音乐"),
            ("gzwx", "工作微信"), ("gongzuoweixin", "工作微信"), ("工作", "工作微信"), ("微信", "工作微信"),
            ("Visual", "Visual Studio"), ("visual", "Visual Studio"), ("studio", "Visual Studio"), ("visualstudio", "Visual Studio"),
            ("visualstudio2026", "Visual Studio 2026"), ("3d", "3DMark"), ("3dmark", "3DMark"),
            ("C++", "C++"), ("+", "C++"), (".NET", ".NET"), ("(测试版)", "微信(测试版)"),
            ("bz", "B站"), ("bzhan", "B站"), ("wyyyy", "网易云音乐"), ("wangyiyunyinyue", "网易云音乐"),
            ("游戏助手", "🎮游戏助手"), ("yxzs", "🎮游戏助手"), ("🎮", "🎮游戏助手"), ("𠮷🪐", "𠮷🪐"),
            ("-a_b.c", "工具-A_B.C")
        })
        {
            quick.SearchQuery = query;
            Check(quick.Results.Any(c => c.DisplayName == target), $"{query} finds {target}");
            Check(quick.Results.Distinct().Count() == quick.Results.Count && quick.Results.Count <= 8, "Unique results within Top8");
        }
        quick.SearchQuery = "qq";
        Check(quick.Results[0].DisplayName == "QQ", "Direct QQ exact match wins");
        quick.SearchQuery = "qqyy";
        Check(quick.Results[0].DisplayName == "QQ音乐", "QQ音乐 is first initials result");
        window.UpdateLayout();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        var surface = (Border)((QuickLauncherOverlay)window.FindName("QuickOverlay")).FindName("PanelSurface");
        await WaitForAsync(() => surface.IsVisible && surface.Opacity >= 0.99);
        foreach (var (query, target) in new[] { ("wx", "微信"), ("jsb", "记事本"), ("qqyy", "QQ音乐") })
        {
            quick.SearchQuery = query;
            await Task.Delay(180); window.UpdateLayout();
            Check(quick.Results[0].DisplayName == target && quick.SelectedIndex == 0, "Default selection for " + query);
            SaveWindow(window, "pinyin-" + query + ".png");
        }

        var wechat = content.Applications.Single(c => c.DisplayName == "微信");
        var originalEntry = search.Index.GetEntry(wechat);
        var unchanged = search.Index.GetEntry(content.Applications.Single(c => c.DisplayName == "记事本"));
        quick.Close();
        wechat.RenameDraft = "办公微信";
        await wechat.RenameCommand.ExecuteAsync(null);
        var renamedEntry = search.Index.GetEntry(wechat);
        Check(renamedEntry is { FullPinyin: "bangongweixin", PinyinInitials: "bgwx" } && !ReferenceEquals(originalEntry, renamedEntry), "Closed overlay rename replaces cached keys");
        quick.Open(); quick.SearchQuery = "bangongweixin";
        Check(quick.Results.Single() == wechat, "Rename searchable immediately after reopen");
        wechat.RenameDraft = "工作微信";
        await wechat.RenameCommand.ExecuteAsync(null);
        Check(quick.Results.Count == 0, "Old renamed key stops matching immediately");
        quick.SearchQuery = "gz wx";
        Check(quick.Results.Contains(wechat), "Open overlay rename updates new initials immediately");
        Check(ReferenceEquals(unchanged, search.Index.GetEntry(content.Applications.Single(c => c.DisplayName == "记事本"))), "Rename leaves unrelated entries unchanged");

        quick.SearchQuery = "jisuanqi";
        await content.ImportAsync([MakeShortcut("计算器", CopyProbe("计算器目标"))]);
        var imported = content.Applications.Last();
        Check(imported.DisplayName.Contains("计算器") && quick.Results.Contains(imported), "New Chinese import searchable without restart");
        Check(search.Index.Count == content.Applications.Count, "Imported application has one index entry");
        await imported.RemoveCommand.ExecuteAsync(null);
        Check(!quick.Results.Contains(imported) && search.Index.GetEntry(imported) is null && search.Index.Count == content.Applications.Count, "Remove clears UI and index without ghost application");

        quick.SearchQuery = "";
        Check(quick.Results.Count == 0, "Empty query still prompts with no recent or favorites");
        await content.Applications[0].ToggleFavoriteCommand.ExecuteAsync(null);
        Check(quick.Results.SequenceEqual([content.Applications[0]]), "Empty query still falls back to favorites");
        var entries = content.Applications.Select(search.Index.GetEntry).ToArray();
        foreach (string query in new[] { "w", "wx", "wei xin", "记事", "qqyy", "+", "." })
        {
            search.Query = query;
            var matches = content.Applications.Where(search.Matches).ToArray();
            Array.Sort(matches, (a, b) => search.Compare(a, b));
        }
        Check(entries.Zip(content.Applications.Select(search.Index.GetEntry)).All(p => ReferenceEquals(p.First, p.Second)), "Queries reuse existing keys without conversion");

        // Candidate number keys must reach the IME rather than launcher commands.
        var overlay = (QuickLauncherOverlay)window.FindName("QuickOverlay");
        var box = (TextBox)overlay.FindName("QueryBox");
        var composition = new TextComposition(InputManager.Current, box, "wei");
        box.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = TextCompositionManager.PreviewTextInputStartEvent });
        var candidateKey = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(box), 0, Key.D1) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        box.RaiseEvent(candidateKey);
        Check(!candidateKey.Handled && quick.IsOpen, "IME candidate digit remains unhandled by launcher");
        box.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = TextCompositionManager.PreviewTextInputEvent });
        quick.Close();
        VerifyPinyinRanking();
        await VerifyPinyinPerformanceAsync(window);
    }

    private static ApplicationCardViewModel PinyinCard(string name, int number = 0, DateTimeOffset? lastLaunch = null)
    {
        var model = new LauncherApplication(Guid.NewGuid(), name, $@"C:\Pinyin\{number}.exe", $@"C:\Pinyin\{number}.exe", null, ApplicationSourceType.Executable) { LastLaunchTime = lastLaunch };
        return new(model, new ApplicationLaunchService(), new RecentMemoryStore([model]));
    }

    private static void VerifyPinyinRanking()
    {
        string[] ordered = ["shi", "shi工具", "工具shi", "s h i", "s h i工具", "工具s h i", "诗", "诗歌", "s好i", "s好i工具", "测试", "as好i"];
        var source = new ObservableCollection<ApplicationCardViewModel>(ordered.Reverse().Select((name, i) => PinyinCard(name, i)));
        using var search = new ApplicationSearchService(source) { Query = "shi" };
        var sorted = source.Where(search.Matches).ToArray();
        Array.Sort(sorted, (a, b) => search.Compare(a, b));
        Check(sorted.Select(c => c.DisplayName).SequenceEqual(ordered), "Direct > normalized > full exact/prefix > initials exact/prefix > full/initials contains");
        search.Query = "does-not-exist";
        Check(!source.Any(search.Matches), "No unmatched item enters results");
        var time = DateTimeOffset.Now;
        var a = PinyinCard("诗甲", lastLaunch: time.AddDays(-1)); var b = PinyinCard("诗乙", lastLaunch: time);
        source.Add(a); source.Add(b); search.Query = "shi";
        Check(search.Compare(b, a) < 0, "Equal rank uses recent launch first");
        var sameTime = PinyinCard(b.DisplayName, lastLaunch: a.LastLaunchTime); source.Add(sameTime);
        Check(Math.Sign(search.Compare(a, sameTime)) == Math.Sign(StringComparer.OrdinalIgnoreCase.Compare(a.DisplayName, sameTime.DisplayName)), "Equal rank/time uses DisplayName");
        var twin = PinyinCard(a.DisplayName, lastLaunch: a.LastLaunchTime); source.Add(twin);
        Check(Math.Sign(search.Compare(a, twin)) == Math.Sign(a.Application.Id.CompareTo(twin.Application.Id)), "Equal rank/time/name uses Id");
        var before = search.Index.GetEntry(a);
        source.Move(source.IndexOf(a), 0);
        Check(ReferenceEquals(before, search.Index.GetEntry(a)), "Moving a card does not rebuild keys");
        source[0] = PinyinCard("记事本");
        Check(search.Index.GetEntry(a) is null && search.Index.GetEntry(source[0])?.PinyinInitials == "jsb", "Replacement drops old entry and builds new entry");
        var all = source.Concat([a]).ToArray();
        source.Clear();
        Check(search.Index.Count == 0, "Collection Reset releases all entries");
        source.Add(a); search.Dispose(); source.Add(b);
        Check(search.Index.Count == 0, "Disposed index unsubscribes from source");
        foreach (var card in all.Distinct()) card.Dispose();
    }

    private static async Task VerifyPinyinPerformanceAsync(MainWindow window)
    {
        foreach (int count in new[] { 300, 1000 })
        {
            var records = Enumerable.Range(0, count).Select(i => new LauncherApplication(Guid.NewGuid(), $"工作微信网易云音乐工具 {i:D4}",
                $@"C:\Pinyin\{i}.exe", $@"C:\Pinyin\{i}.exe", null, ApplicationSourceType.Executable)
                { LastLaunchTime = i < 10 ? DateTimeOffset.Now.AddMinutes(-i) : null }).ToArray();
            var store = new RecentMemoryStore(records);
            using var content = new ContentViewModel(new ApplicationImportService(new ShortcutResolver(), new ApplicationIconService()),
                new ApplicationLaunchService(), store, new ToastService());
            var timer = Stopwatch.StartNew();
            await content.InitializeAsync();
            double loadMs = timer.Elapsed.TotalMilliseconds;
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            timer.Restart();
            using var search = new ApplicationSearchService(content.Applications);
            double indexMs = timer.Elapsed.TotalMilliseconds;
            long buildBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            long chars = content.Applications.Select(search.Index.GetEntry).Sum(e => (long)(e!.NormalizedName.Length + e.FullPinyin.Length + e.PinyinInitials.Length));
            var entries = content.Applications.Select(search.Index.GetEntry).ToArray();
            window.DataContext = new MainWindowViewModel(content);
            var quick = content.QuickLauncher; quick.Open(); window.UpdateLayout();
            var queryMs = new List<double>(); var projectionMs = new List<double>(); var layoutMs = new List<double>();
            foreach (string query in new[] { "wx", "gzwx", "gongzuoweixin", "wangyiyun", "yinyue", "0009", "不存在" })
            {
                timer.Restart(); search.Query = query;
                var results = content.Applications.Where(search.Matches).ToArray();
                Array.Sort(results, (a, b) => search.Compare(a, b));
                _ = results.Take(8).ToArray(); queryMs.Add(timer.Elapsed.TotalMilliseconds);
                timer.Restart(); quick.SearchQuery = query; projectionMs.Add(timer.Elapsed.TotalMilliseconds);
                timer.Restart(); window.UpdateLayout(); await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render); layoutMs.Add(timer.Elapsed.TotalMilliseconds);
                Check(quick.Results.Count <= 8 && (query == "不存在" ? quick.Results.Count == 0 : quick.Results.Count > 0), $"{count} Chinese applications: {query} results");
            }
            Console.WriteLine($"{count} apps: load/index via Content {loadMs:F2} ms; isolated warm index {indexMs:F2} ms; build allocated {buildBytes:N0} bytes; derived UTF16 payload {chars * 2:N0} bytes");
            Console.WriteLine($"{count} cached match/rank ms: " + string.Join(", ", queryMs.Select(x => x.ToString("F3"))));
            Console.WriteLine($"{count} projection/binding ms: " + string.Join(", ", projectionMs.Select(x => x.ToString("F2"))));
            Console.WriteLine($"{count} layout/render ms: " + string.Join(", ", layoutMs.Select(x => x.ToString("F2"))));
            Check(entries.Zip(content.Applications.Select(search.Index.GetEntry)).All(p => ReferenceEquals(p.First, p.Second)), "Performance queries never regenerate keys");
            Check(store.Loads == 1 && search.Index.Count == count, "Search only reads cached collection, no storage reload");
            quick.SearchQuery = "";
            Check(quick.Results.Count == 6 && quick.Results[0].Application == records[0], "Recent Top6 unchanged");
            quick.Close();
            window.DataContext = null;
        }
    }
}
