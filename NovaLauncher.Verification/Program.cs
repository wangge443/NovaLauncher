using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NovaLauncher.App.Models;
using NovaLauncher.App.Services;
using NovaLauncher.App.ViewModels;
using NovaLauncher.App.Views;

namespace NovaLauncher.Verification;

internal static partial class Program
{
    private static int _checks;
    private static string _fixtures = "";
    private static string _screenshots = "";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 5 && args[0] == "--external-drop") return RunExternalDrop(args);
        if (args.Contains("--logging")) return RunLoggingTestsAsync().GetAwaiter().GetResult();
        if (args.Contains("--release-safety")) return RunReleaseSafetyAsync().GetAwaiter().GetResult();
        if (args.Contains("--real-app-edit-migration")) return VerifyRealEditingMigrationAsync().GetAwaiter().GetResult();
        if (args.Length == 2 && args[0] == "--app-edit-restart") return VerifyEditingRestartAsync(args[1]).GetAwaiter().GetResult();
        if (args.Contains("--real-appearance")) return VerifyRealAppearanceAsync().GetAwaiter().GetResult();
        if (args.Length == 2 && args[0] == "--appearance-real-window") return RunRealAppearanceWindow(args[1]);
        if (args.Contains("--settings-preview")) return RunSettingsPreview(args.Contains("--verify-shell"));
        if (args.Contains("--real-settings")) return VerifyRealSettingsAsync().GetAwaiter().GetResult();
        if (args.Length == 2 && args[0] == "--settings-real-window") return RunRealSettingsWindow(args[1]);
        if (args.Contains("--inspect-db")) return InspectDatabase();
        if (args.Contains("--real-profile")) return VerifyRealProfileAsync().GetAwaiter().GetResult();
        if (args.Length == 2 && args[0] == "--profile-real-run") return RunProfileWindow(args[1], true);
        if (args.Length == 2 && args[0] == "--profile-real-check") return RunProfileWindow(args[1], false);
        if (args.Contains("--real-home")) return VerifyRealHomeAsync().GetAwaiter().GetResult();
        if (args.Contains("--home-preview")) return RunHomeWindow("", false);
        if (args.Length == 2 && args[0] == "--home-real-run") return RunHomeWindow(args[1], true);
        if (args.Length == 2 && args[0] == "--home-real-check") return RunHomeWindow(args[1], false);
        if (args.Contains("--real-recent")) return VerifyRealRecentAsync().GetAwaiter().GetResult();
        if (args.Length == 2 && args[0] == "--recent-restart") return RunRecentWindow(args[1], false, false);
        if (args.Length == 2 && args[0] == "--recent-real-run") return RunRecentWindow(args[1], true, true);
        if (args.Length == 2 && args[0] == "--recent-real-check") return RunRecentWindow(args[1], true, false);
        if (args.Contains("--real-favorites")) return VerifyRealFavoritesAsync().GetAwaiter().GetResult();
        if (args.Length == 2 && args[0] == "--favorite-restart") return RunFavoriteWindow(args[1], false, false);
        if (args.Length == 2 && args[0] == "--favorite-real-set") return RunFavoriteWindow(args[1], true, true);
        if (args.Length == 2 && args[0] == "--favorite-real-check") return RunFavoriteWindow(args[1], true, false);
        if (args.Contains("--migrations")) return RunMigrationTestsAsync().GetAwaiter().GetResult();
        if (args.Contains("--real-migration")) return VerifyRealMigrationAsync().GetAwaiter().GetResult();
        if (args.Contains("--real-categories")) return VerifyRealCategoriesAsync().GetAwaiter().GetResult();
        if (args.Length == 2 && args[0] == "--category-real-window") return RunRealCategoryWindow(args[1]);
        if (args.Length == 2 && args[0] == "--category-restart") return RunCategoryRestart(args[1]);
        if (args.Length == 2 && args[0] == "--migration-window") return RunMigrationWindow(args[1]);
        if (args.Length >= 2 && args[0].StartsWith("--restart-")) return RunRestartProcess(args[0], args[1]);
        bool launchTests = args.Contains("--launch");
        bool persistenceTests = args.Contains("--persistence");
        bool toastTests = args.Contains("--toast");
        bool categoryTests = args.Contains("--categories");
        bool favoriteTests = args.Contains("--favorites");
        bool recentTests = args.Contains("--recent");
        bool homeTests = args.Contains("--home");
        bool polishTests = args.Contains("--polish");
        bool profileTests = args.Contains("--profile"); bool settingsTests = args.Contains("--settings"); bool appearanceTests = args.Contains("--appearance");
        _screenshots = Path.GetFullPath(args.FirstOrDefault(arg => !arg.StartsWith("--")) ??
            (persistenceTests ? "docs/screenshots/4.3" : launchTests ? "docs/screenshots/4.2" : "docs/screenshots/4.1"));
        _fixtures = Path.Combine(Path.GetTempPath(), "NovaLauncher-Verification-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_fixtures);
        Directory.CreateDirectory(_screenshots);
        Point originalCursor = Native.CursorPosition();
        var app = new NovaLauncher.App.App(CreatePersistence(Path.Combine(_fixtures, "window-data")));
        app.InitializeComponent();
        app.Startup += (_, _) => app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                if (args.Contains("--pinyin")) await RunPinyinAsync((MainWindow)app.MainWindow);
                else if (args.Contains("--quick-launcher")) await RunQuickLauncherAsync((MainWindow)app.MainWindow);
                else if (args.Contains("--app-edit")) await RunApplicationEditingAsync((MainWindow)app.MainWindow);
                else if (args.Contains("--management")) await RunManagementAsync((MainWindow)app.MainWindow); else if (appearanceTests) await RunAppearanceAsync((MainWindow)app.MainWindow); else if (settingsTests) await RunSettingsAsync((MainWindow)app.MainWindow); else if (profileTests) await RunProfileAsync((MainWindow)app.MainWindow);
                else if (polishTests) await RunPolishAsync((MainWindow)app.MainWindow);
                else if (homeTests) await RunHomeAsync((MainWindow)app.MainWindow);
                else if (recentTests) await RunRecentAsync((MainWindow)app.MainWindow);
                else if (favoriteTests) await RunFavoritesAsync((MainWindow)app.MainWindow);
                else if (categoryTests) await RunCategoriesAsync((MainWindow)app.MainWindow);
                else if (toastTests) await RunToastAsync((MainWindow)app.MainWindow);
                else if (persistenceTests) await RunPersistenceAsync((MainWindow)app.MainWindow);
                else if (launchTests) await RunLaunchAsync((MainWindow)app.MainWindow);
                else await RunAsync((MainWindow)app.MainWindow);
                Console.WriteLine($"PASS: {_checks} assertions; screenshots: {_screenshots}");
                app.Shutdown(0);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                app.Shutdown(1);
            }
        }));
        try { return app.Run(); }
        finally
        {
            Directory.Delete(_fixtures, true);
            Native.SetCursorPos((int)originalCursor.X, (int)originalCursor.Y);
        }
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        _checks++;
        Console.WriteLine("PASS: " + label);
    }

    private static async Task RunAsync(MainWindow window)
    {
        var service = new ApplicationImportService(new ShortcutResolver(), new ApplicationIconService());
        string executable = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        string shortcut = MakeShortcut("记事本快捷方式", executable);
        string broken = Path.Combine(_fixtures, "broken.lnk");
        File.WriteAllText(broken, "broken shortcut");
        string text = Path.Combine(_fixtures, "unsupported.txt");
        File.WriteAllText(text, "test");
        string fake = Path.Combine(_fixtures, "fake.exe");
        File.WriteAllText(fake, "not a PE executable");

        var exe = (await service.ImportAsync(executable, default)).Application;
        Check(exe is not null && exe.SourceType == ApplicationSourceType.Executable, "EXE import");
        var version = FileVersionInfo.GetVersionInfo(executable);
        string expected = version.FileDescription!.Trim(); // Windows' shared OS product name is not an app name.
        Check(exe!.DisplayName == expected, "EXE product metadata name");
        Check(exe.Icon is { Length: > 0 }, "EXE native icon extracted");
        var bitmap = (BitmapSource)new ApplicationCardViewModel(exe, new ApplicationLaunchService(), new MemoryPersistence()).Icon;
        Check(bitmap.PixelWidth >= 72 && bitmap.IsFrozen, "High-resolution frozen WPF icon");
        var link = (await service.ImportAsync(shortcut, default)).Application;
        Check(link is not null && link.ExecutablePath.Equals(executable, StringComparison.OrdinalIgnoreCase)
            && link.DisplayName == "记事本快捷方式" && link.SourcePath == shortcut
            && link.SourceType == ApplicationSourceType.Shortcut, "LNK target/name/source");

        foreach (string invalid in new[] { broken, text, fake, Path.Combine(_fixtures, "missing.exe"),
            MakeShortcut("missing target", Path.Combine(_fixtures, "absent.exe")),
            MakeShortcut("document target", text), MakeShortcut("folder target", _fixtures) })
            Check((await service.ImportAsync(invalid, default)).Application is null, "Safe rejection: " + Path.GetFileName(invalid));

        // A copy with its resource directory removed is still a PE executable but has no icon/version.
        string noIcon = Path.Combine(_fixtures, "无图标程序.exe");
        byte[] bytes = File.ReadAllBytes(executable);
        int peOffset = BitConverter.ToInt32(bytes, 0x3c);
        int optional = peOffset + 24;
        int directories = optional + (BitConverter.ToUInt16(bytes, optional) == 0x20b ? 112 : 96);
        Array.Clear(bytes, directories + 16, 8);
        File.WriteAllBytes(noIcon, bytes);
        var fallback = (await service.ImportAsync(noIcon, default)).Application;
        Check(fallback is not null && fallback.Icon is null && fallback.DisplayName == "无图标程序", "No resources: filename and default icon fallback");
        Check(new ApplicationCardViewModel(fallback!, new ApplicationLaunchService(), new MemoryPersistence()).Icon is DrawingImage { IsFrozen: true }, "Default Nova vector icon");

        using (var vm = new ContentViewModel(service, new ApplicationLaunchService(), new MemoryPersistence(), new ToastService()))
        {
            await vm.ImportAsync([executable, shortcut, executable.ToUpperInvariant()]);
            Check(vm.Applications.Count == 1 && vm.Toast.IsOpen && vm.Toast.Message == "该应用已添加", "EXE/LNK and case-insensitive duplicates");
            await vm.ImportAsync([text, noIcon]);
            Check(vm.Applications.Count == 2 && vm.StatusMessage!.Contains("不支持此文件类型"), "Mixed batch preserves valid imports and errors");
        }
        var waiting = new WaitingImporter();
        using (var vm = new ContentViewModel(waiting, new ApplicationLaunchService(), new MemoryPersistence(), new ToastService()))
        {
            Task first = vm.ImportAsync([executable]);
            await vm.ImportAsync([executable]);
            Check(waiting.Calls == 1, "Concurrent drops do not start duplicate batches");
            vm.Dispose();
            await first;
            Check(vm.Applications.Count == 0 && !vm.IsImporting, "Close cancels pending import");
        }

        // Warm up before measuring USER objects; each extraction must destroy its owned HICON.
        var icons = new ApplicationIconService();
        icons.Extract(executable);
        int before = Native.GetGuiResources(Process.GetCurrentProcess().Handle, 1);
        for (int i = 0; i < 80; i++) icons.Extract(executable);
        int after = Native.GetGuiResources(Process.GetCurrentProcess().Handle, 1);
        Check(after <= before + 2, $"HICON resource stress ({before} -> {after})");

        window.Activate();
        window.Topmost = true;
        await Task.Delay(350);
        var content = Descendants<ContentView>(window).Single();
        var contentVm = ((MainWindowViewModel)window.DataContext).Content;
        Check(contentVm.IsEmpty, "Actual window starts empty");
        SaveWindow(window, "empty.png");
        await DropAsync(window, content, [shortcut], "drag.png");
        await WaitForAsync(() => contentVm.Applications.Count == 1 && !contentVm.IsImporting);
        Check(!contentVm.IsEmpty && contentVm.Applications[0].DisplayName == "记事本快捷方式",
            $"OLE file drop creates bound shortcut card ({contentVm.Applications[0].DisplayName}; {contentVm.Applications[0].Application.SourcePath})");
        await DropAsync(window, content, [executable]);
        await WaitForAsync(() => !contentVm.IsImporting);
        Check(contentVm.Applications.Count == 1, "Actual OLE duplicate is ignored");
        await DropAsync(window, content, [text]);
        Check(contentVm.StatusMessage == "不支持此文件类型" && !contentVm.IsDragOver,
            $"Unsupported OLE drop feedback and reset (status={contentVm.StatusMessage}, over={contentVm.IsDragOver})");
        await DropAsync(window, content, [executable], cancel: true);
        Check(!contentVm.IsDragOver, "OLE cancellation clears Workspace highlight");
        await DropAsync(window, content, [executable], cancel: true, leave: true);
        Check(!contentVm.IsDragOver, "Leaving Workspace clears highlight");

        string longName = MakeShortcut("用于检查名称换行与完整提示的应用程序快捷方式", Path.Combine(Environment.SystemDirectory, "cmd.exe"));
        string[] candidates = [noIcon, longName, Path.Combine(Environment.SystemDirectory, "WindowsPowerShell/v1.0/powershell.exe"),
            Path.Combine(Environment.SystemDirectory, "mmc.exe"), Path.Combine(Environment.SystemDirectory, "mstsc.exe"),
            Path.Combine(Environment.SystemDirectory, "taskmgr.exe"), Path.Combine(Environment.SystemDirectory, "regedit.exe")];
        // regedit is in Windows, not System32 on some systems.
        candidates = candidates.Where(File.Exists).ToArray();
        await DropAsync(window, content, candidates);
        await WaitForAsync(() => contentVm.Applications.Count == candidates.Length + 1 && !contentVm.IsImporting);
        Check(contentVm.Applications.Count >= 7, "Multiple real executables imported");
        await Task.Delay(4200);
        window.UpdateLayout();
        var cards = Descendants<ApplicationCardView>(content).ToArray();
        Check(cards.Length == contentVm.Applications.Count, "All cards rendered");
        Check(cards.All(card => card.ActualHeight == cards[0].ActualHeight), "Short and long names preserve equal card heights");
        var longCard = cards.Single(card => ((ApplicationCardViewModel)card.DataContext).DisplayName.StartsWith("用于检查"));
        await CheckTooltipAsync(window, cards[0], new Point(60, 45), "Short name: card hover shows complete DisplayName");
        await CheckTooltipAsync(window, longCard, new Point(60, 45), "Long name: card hover shows complete DisplayName");
        await CheckTooltipAsync(window, longCard, new Point(60, 115), "Long name: name hover shows complete DisplayName");
        var fullRows = cards.Select(c => Math.Round(c.TranslatePoint(new Point(), content).Y)).Distinct().Count();
        Check(fullRows > 1, "Default width wraps cards");
        Point caption = window.PointToScreen(new Point(100, 24));
        Native.SetCursorPos((int)caption.X, (int)caption.Y);
        await Task.Delay(250);
        SaveWindow(window, "applications.png");
        var cardPoint = cards[0].PointToScreen(new Point(60, 60));
        Native.SetCursorPos((int)cardPoint.X, (int)cardPoint.Y);
        await Task.Delay(250);
        SaveWindow(window, "hover.png");
        Check(cards[0].IsMouseOver, "Actual card hover");
        window.Width = 800;
        window.Height = 560;
        await Task.Delay(350);
        window.UpdateLayout();
        int narrowRows = cards.Select(c => Math.Round(c.TranslatePoint(new Point(), content).Y)).Distinct().Count();
        Check(narrowRows > fullRows, "Minimum window width increases wrapped rows");
        var scroll = Descendants<ScrollViewer>(content).Single();
        Check(scroll.ScrollableWidth == 0 && scroll.ViewportWidth > 0, "No horizontal overflow");
        SaveWindow(window, "small.png");
        window.Width = 1400;
        window.Height = 800;
        await Task.Delay(350);
        window.UpdateLayout();
        int wideRows = cards.Select(c => Math.Round(c.TranslatePoint(new Point(), content).Y)).Distinct().Count();
        Check(wideRows < narrowRows, "Wide window reflows cards");
        SaveWindow(window, "wide.png");
        Application.Current.Resources["HoverDuration"] = new Duration(TimeSpan.Zero);
        cards[0].RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
        var lift = (TranslateTransform)((Grid)cards[0].Content).RenderTransform;
        Check(lift.Y == 0, "Reduced motion removes card lift");
    }

    private static string MakeShortcut(string name, string target)
    {
        string path = Path.Combine(_fixtures, name + ".lnk");
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        object? shortcut = null;
        try
        {
            dynamic automation = shell;
            shortcut = automation.CreateShortcut(path);
            dynamic link = shortcut;
            link.TargetPath = target;
            link.Save();
            return path;
        }
        finally
        {
            if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut);
            Marshal.FinalReleaseComObject(shell);
        }
    }

    private static async Task CheckTooltipAsync(Window window, ApplicationCardView card, Point position, string label)
    {
        Point outside = window.PointToScreen(new Point(100, 24));
        Native.SetCursorPos((int)outside.X, (int)outside.Y);
        await Task.Delay(300);
        Point inside = card.PointToScreen(position);
        Native.SetCursorPos((int)inside.X, (int)inside.Y);
        var tooltip = (ToolTip)card.ToolTip;
        await WaitForAsync(() => tooltip.IsOpen);
        Check(Equals(tooltip.Content, ((ApplicationCardViewModel)card.DataContext).DisplayName), label);
        Native.SetCursorPos((int)outside.X, (int)outside.Y);
        await WaitForAsync(() => !tooltip.IsOpen);
    }

    private static async Task DropAsync(MainWindow window, ContentView target, string[] paths, string? capture = null, bool cancel = false, bool leave = false)
    {
        // Use real OLE/CF_HDROP with a controlled source lifecycle and pointer movement.
        var source = new Window { Width = 160, Height = 120, Left = 0, Top = 0, WindowStyle = WindowStyle.ToolWindow,
            ShowInTaskbar = false, Topmost = true, Content = new TextBlock { Text = "拖放验证" } };
        Point original = Native.CursorPosition();
        source.Show();
        await Task.Delay(100);
        var data = new DataObject(DataFormats.FileDrop, paths);
        var origin = source.PointToScreen(new Point(30, 20));
        var destination = target.PointToScreen(new Point(target.ActualWidth / 2, target.ActualHeight / 2));
        Native.SetCursorPos((int)origin.X, (int)origin.Y);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        int step = 0;
        bool finish = false;
        // Own the test source's drag lifecycle; physical button polling is unreliable under desktop automation.
        source.QueryContinueDrag += (_, e) =>
        {
            e.Action = finish ? (cancel ? DragAction.Cancel : DragAction.Drop) : DragAction.Continue;
            e.Handled = true;
        };
        Console.WriteLine($"OLE begin: {paths[0]}, target={destination}");
        timer.Tick += (_, _) =>
        {
            if (++step == 1) Native.SetCursorPos((int)destination.X, (int)destination.Y);
            if (step == 2)
            {
                if (capture is not null) SaveWindow(window, capture);
                if (leave) Native.SetCursorPos((int)origin.X, (int)origin.Y);
            }
            if (step == 3)
            {
                finish = true;
                Point finishPoint = leave ? origin : destination;
                Native.SetCursorPos((int)finishPoint.X + 1, (int)finishPoint.Y);
            }
            if (step >= 6) Native.SetCursorPos((int)destination.X + step, (int)destination.Y);
        };
        timer.Start();
        try
        {
            DragDropEffects effect = DragDrop.DoDragDrop(source, data, DragDropEffects.Copy);
            Console.WriteLine($"OLE: {Path.GetFileName(paths[0])}, effect={effect}, ticks={step}");
        }
        finally
        {
            timer.Stop();
            source.Close();
            Native.SetCursorPos((int)original.X, (int)original.Y);
        }
        await Task.Delay(200);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("Verification condition did not complete within 20 seconds");
            await Task.Delay(50);
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void SaveWindow(Window window, string name)
    {
        window.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(_screenshots, name));
        encoder.Save(stream);
    }

    private sealed class WaitingImporter : IApplicationImportService
    {
        public int Calls { get; private set; }
        public async Task<ApplicationImportResult> ImportAsync(string path, CancellationToken cancellationToken)
        {
            Calls++;
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new(null, "unused");
        }
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X; public int Y; }
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);
        public static Point CursorPosition() { GetCursorPos(out var point); return new(point.X, point.Y); }
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern int GetGuiResources(IntPtr process, uint flags);
    }
}
