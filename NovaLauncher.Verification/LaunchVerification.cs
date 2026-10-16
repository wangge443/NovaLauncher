using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using NovaLauncher.App.Models;
using NovaLauncher.App.Services;
using NovaLauncher.App.ViewModels;
using NovaLauncher.App.Views;

namespace NovaLauncher.Verification;

internal static partial class Program
{
    private static async Task RunLaunchAsync(MainWindow window)
    {
        string output = Path.Combine(_fixtures, "results");
        Directory.CreateDirectory(output);
        string? previousOutput = Environment.GetEnvironmentVariable("NOVA_LAUNCH_TEST_OUTPUT");
        Environment.SetEnvironmentVariable("NOVA_LAUNCH_TEST_OUTPUT", output);
        try { await VerifyLaunchAsync(window, output); }
        finally
        {
            Environment.SetEnvironmentVariable("NOVA_LAUNCH_TEST_OUTPUT", previousOutput);
            // Child probes exit immediately; wait here only so test fixtures can be removed safely.
            foreach (var record in ReadProbes(output))
            {
                try
                {
                    using var process = Process.GetProcessById(record.ProcessId);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (ArgumentException) { /* Probe has already exited. */ }
            }
        }
    }

    private static async Task VerifyLaunchAsync(MainWindow window, string output)
    {
        string exe = CopyProbe("普通应用");
        string linkTarget = CopyProbe("快捷方式目标");
        string working = Path.Combine(_fixtures, "快捷方式工作目录");
        Directory.CreateDirectory(working);
        string shortcut = MakeConfiguredShortcut(linkTarget, working);
        var importer = new ApplicationImportService(new ShortcutResolver(), new ApplicationIconService());
        var launcher = new ApplicationLaunchService();
        var model = (await importer.ImportAsync(exe, default)).Application!;
        var shortcutModel = (await importer.ImportAsync(shortcut, default)).Application!;
        Check(model is not null && shortcutModel is not null, "Stable importer accepts both launch fixtures");

        DateTimeOffset now = DateTimeOffset.Now;
        Check(LaunchTimeFormatter.Format(null, now) == "从未", "Never-launched time");
        Check(LaunchTimeFormatter.Format(now.AddSeconds(-10), now) == "刚刚", "Just-now time");
        Check(LaunchTimeFormatter.Format(now.AddMinutes(-5), now) == "5分钟前", "Relative minutes");
        var noon = new DateTimeOffset(now.Date.AddHours(12), now.Offset);
        Check(LaunchTimeFormatter.Format(noon.AddHours(-2), noon) == "今天 10:00", "Today time");
        Check(LaunchTimeFormatter.Format(noon.AddDays(-1), noon) == "昨天 12:00", "Yesterday time");
        Check(!LaunchTimeFormatter.Format(noon.AddDays(-3), noon).Contains(':'), "Older date without full timestamp");

        var fake = new ControlledLauncher();
        using (var card = new ApplicationCardViewModel(model!, fake, new MemoryPersistence()))
        using (var other = new ApplicationCardViewModel(shortcutModel!, fake, new MemoryPersistence()))
        {
            Check(card.LaunchCount == 0 && card.LastLaunchTime is null, "Initial model statistics");
            Task first = card.LaunchCommand.ExecuteAsync(null);
            for (int i = 0; i < 30; i++) await card.LaunchCommand.ExecuteAsync(null);
            Task second = other.LaunchCommand.ExecuteAsync(null);
            Check(fake.Calls == 2, "30 rapid same-card requests guarded; different card independent");
            Check(card.LaunchCount == 0, "No count until service confirms success");
            fake.Complete(new(true));
            await Task.WhenAll(first, second);
            Check(card.LaunchCount == 1 && card.Application.LastLaunchTime is not null, "Success stored in serializable model");
            DateTimeOffset? last = card.LastLaunchTime;
            fake.Reset(new(false, "无法启动该应用"));
            string? message = null;
            card.LaunchFailed += value => message = value;
            await card.LaunchCommand.ExecuteAsync(null);
            Check(card.LaunchCount == 1 && card.LastLaunchTime == last && message == "无法启动该应用", "Failure preserves previous statistics and reports error");
            fake.Exception = new Win32Exception(5, "Access denied");
            await card.LaunchCommand.ExecuteAsync(null);
            Check(card.LaunchCount == 1 && !card.IsLaunching, "Permission exception boundary is safe and guard releases");
        }

        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel();
            bool cancelled = false;
            try { await launcher.LaunchAsync(model!, cancel.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && ReadProbes(output).Length == 0, "Cancelled request starts no child process");
        }

        window.Topmost = true;
        window.WindowState = WindowState.Normal;
        window.Show();
        // The command host can pass SW_HIDE in STARTUPINFO; explicitly show the test's native HWND.
        LaunchNative.ShowWindow(new WindowInteropHelper(window).Handle, 1);
        window.Activate();
        LaunchNative.SetForegroundWindow(new WindowInteropHelper(window).Handle);
        var content = Descendants<ContentView>(window).Single();
        var vm = ((MainWindowViewModel)window.DataContext).Content;
        await vm.ImportAsync([exe, shortcut]);
        await Task.Delay(300);
        var cards = AllCardViews(content);
        Check(cards.Length == 2, "Two launch cards rendered");
        var firstCard = vm.Applications[0];
        var secondCard = vm.Applications[1];
        var point = cards[0].PointToScreen(new Point(60, 60));
        LaunchNative.SetWindowPos(new WindowInteropHelper(window).Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13);
        Native.SetCursorPos((int)point.X, (int)point.Y);
        await Task.Delay(250);
        Check(LaunchNative.WindowFromPoint(new((int)point.X, (int)point.Y)) == new WindowInteropHelper(window).Handle,
            "Input target is the unobstructed verification window");
        Check(firstCard.LastLaunchText == "上次启动：从未" && firstCard.LaunchCountText == "启动次数：0", "Never-launched hover state");
        SaveWindow(window, "never-launched.png");
        double height = cards[0].ActualHeight;
        LaunchNative.SetForegroundWindow(new WindowInteropHelper(window).Handle);
        LaunchNative.mouse_event(2, 0, 0, 0, UIntPtr.Zero);
        LaunchNative.mouse_event(4, 0, 0, 0, UIntPtr.Zero);
        await Task.Delay(600);
        await WaitForAsync(() => firstCard.LaunchCount == 1 && !firstCard.IsLaunching && ReadProbes(output).Length == 1);
        var result = ReadProbes(output).Single();
        Check(result.WorkingDirectory.Equals(Path.GetDirectoryName(exe), StringComparison.OrdinalIgnoreCase)
            && result.Arguments.Length == 0, "Single mouse click launches EXE with executable working directory");
        Check(firstCard.LastLaunchTime is { } timestamp && timestamp >= now && firstCard.LastLaunchText.Contains("刚刚"), "Real launch refreshes local time");

        // Use native key events on the real Button to exercise its Enter/Space semantics.
        Button button = (Button)cards[0].FindName("LaunchButton");
        Check(button.Focus(), "Launch card accepts keyboard focus");
        LaunchNative.keybd_event(0x0D, 0, 0, UIntPtr.Zero);
        LaunchNative.keybd_event(0x0D, 0, 2, UIntPtr.Zero);
        await WaitForAsync(() => firstCard.LaunchCount == 2 && !firstCard.IsLaunching);
        Check(firstCard.LaunchCount == 2, "Enter launches again");
        button.Focus();
        LaunchNative.keybd_event(0x20, 0, 0, UIntPtr.Zero);
        LaunchNative.keybd_event(0x20, 0, 2, UIntPtr.Zero);
        await WaitForAsync(() => firstCard.LaunchCount == 3 && !firstCard.IsLaunching);
        Check(firstCard.LaunchCount == 3, "Space launches again");

        await secondCard.LaunchCommand.ExecuteAsync(null);
        await WaitForAsync(() => ReadProbes(output).Length == 4);
        var configured = ReadProbes(output).Single(record => record.Arguments.Length > 0);
        Check(configured.Arguments.SequenceEqual(new[] { "--from-shortcut", "带空格的参数 value" }) &&
            configured.WorkingDirectory.Equals(working, StringComparison.OrdinalIgnoreCase), "LNK Shell preserves arguments and configured working directory");
        Check(secondCard.LaunchCount == 1, "Shortcut success counted independently");

        int childrenBefore = ReadProbes(output).Length;
        point = cards[0].PointToScreen(new Point(60, 60));
        Native.SetCursorPos((int)point.X, (int)point.Y);
        for (int i = 0; i < 12; i++)
        {
            LaunchNative.mouse_event(2, 0, 0, 0, UIntPtr.Zero);
            LaunchNative.mouse_event(4, 0, 0, 0, UIntPtr.Zero);
        }
        await WaitForAsync(() => firstCard.LaunchCount == 4 && !firstCard.IsLaunching);
        await Task.Delay(300);
        Check(ReadProbes(output).Length == childrenBefore + 1, "12 rapid native clicks start exactly one child");
        SaveWindow(window, "launched-hover.png");
        Check(cards[0].ActualHeight == height && cards[1].ActualHeight == height, "Hover never changes card height");

        File.Delete(shortcut);
        await secondCard.LaunchCommand.ExecuteAsync(null);
        Check(secondCard.LaunchCount == 1 && vm.StatusMessage!.Contains("无法启动"), "Deleted LNK shows lightweight error, no statistics change");
        // All previously launched probes exit before their disposable copies are deleted.
        await Task.Delay(300);
        File.Delete(exe);
        await firstCard.LaunchCommand.ExecuteAsync(null);
        Check(firstCard.LaunchCount == 4 && vm.StatusMessage!.Contains("无法启动"), "Deleted EXE shows lightweight error, no crash");
        SaveWindow(window, "launch-error.png");

        string invalid = Path.Combine(_fixtures, "invalid.exe");
        File.WriteAllText(invalid, "not a Windows executable");
        var invalidResult = await launcher.LaunchAsync(model! with { ExecutablePath = invalid });
        Check(!invalidResult.Succeeded, "Windows rejection returns failure safely");

        // More fixture copies exercise the actual grid wrapping without launching extra applications.
        for (int i = 0; i < 5; i++) await vm.ImportAsync([CopyProbe("布局测试" + i)]);
        await Task.Delay(4200);
        window.Width = 800;
        window.Height = 560;
        window.UpdateLayout();
        await Task.Delay(200);
        cards = AllCardViews(content);
        int rows = cards.Select(card => Math.Round(card.TranslatePoint(new Point(), content).Y)).Distinct().Count();
        point = cards[1].PointToScreen(new Point(60, 60));
        Native.SetCursorPos((int)point.X, (int)point.Y);
        await Task.Delay(250);
        Check(rows >= 3 && cards.All(card => card.ActualHeight == height), "Small window wraps fixed-size hover cards");
        SaveWindow(window, "small-hover.png");
        window.Width = 1400;
        window.Height = 800;
        window.UpdateLayout();
        await Task.Delay(250);
        int wideRows = cards.Select(card => Math.Round(card.TranslatePoint(new Point(), content).Y)).Distinct().Count();
        Check(wideRows < rows, "Wide window reflows without overlapping cards");
        SaveWindow(window, "wide.png");
        Check(ReadProbes(output).Length == 5, "All failure cases started no extra process");
    }

    private static string CopyProbe(string name)
    {
        string source = Path.GetFullPath("NovaLauncher.LaunchProbe/bin/Release/net9.0-windows");
        string destination = Path.Combine(_fixtures, name);
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source).Where(path => !path.EndsWith(".pdb")))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        return Path.Combine(destination, "NovaLauncher.LaunchProbe.exe");
    }

    private static string MakeConfiguredShortcut(string target, string working)
    {
        string path = Path.Combine(_fixtures, "保留参数的快捷方式.lnk");
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        object? shortcut = null;
        try
        {
            dynamic automation = shell;
            shortcut = automation.CreateShortcut(path);
            dynamic link = shortcut;
            link.TargetPath = target;
            link.Arguments = "--from-shortcut \"带空格的参数 value\"";
            link.WorkingDirectory = working;
            link.Save();
            return path;
        }
        finally
        {
            if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut);
            Marshal.FinalReleaseComObject(shell);
        }
    }

    private sealed record ProbeRecord(string[] Arguments, string WorkingDirectory, string ExecutablePath, int ProcessId);
    private static ProbeRecord[] ReadProbes(string directory) => Directory.GetFiles(directory, "*.json")
        .Select(path => JsonSerializer.Deserialize<ProbeRecord>(File.ReadAllText(path))!).ToArray();

    private sealed class ControlledLauncher : IApplicationLaunchService
    {
        private TaskCompletionSource<ApplicationLaunchResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public Exception? Exception { get; set; }
        public Task<ApplicationLaunchResult> LaunchAsync(LauncherApplication application, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Exception is not null) throw Exception;
            return _completion.Task.WaitAsync(cancellationToken);
        }
        public void Complete(ApplicationLaunchResult result) => _completion.SetResult(result);
        public void Reset(ApplicationLaunchResult result)
        {
            _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _completion.SetResult(result);
        }
    }

    private static class LaunchNative
    {
        [StructLayout(LayoutKind.Sequential)] public record struct NativePoint(int X, int Y);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(NativePoint point);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
        [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    }
}
