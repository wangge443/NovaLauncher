using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NovaLauncher.App.Persistence;
using NovaLauncher.App.ViewModels;
using NovaLauncher.App.Views;

namespace NovaLauncher.App.Diagnostics;

/// <summary>Opt-in isolated process probe. Never runs against the production data directory.</summary>
internal static class PerformanceSession
{
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    public static async Task RunAsync(Window window, ContentViewModel content)
    {
        try
        {
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            PerformanceTrace.Mark("StartupReady");
            var cards = Descendants<ApplicationCardView>(window).ToArray();
            int firstCount = cards.Length;
            string root = ApplicationDataPaths.VerificationRoot!;
            Save(window, Path.Combine(root, "first.png"));
            var home = Descendants<AllApplicationsView>(window).Single();
            var completion = Stopwatch.StartNew();
            while (!home.IsFullyRendered)
            {
                if (completion.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Home completion");
                await Task.Delay(20);
            }
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
            PerformanceTrace.Mark("AllCardsReady");
            await Task.Delay(1000);
            window.UpdateLayout();
            cards = Descendants<ApplicationCardView>(window).ToArray();
            var scroll = Descendants<AllApplicationsView>(window).Select(x => (ScrollViewer)x.FindName("ApplicationsScroll")).Single();
            int layouts = 0;
            EventHandler handler = (_, _) => layouts++;
            window.LayoutUpdated += handler;
            using var process = Process.GetCurrentProcess();
            var cpu = process.TotalProcessorTime;
            int[] gcBefore = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
            long phase = PerformanceTrace.Start();
            for (int i = 0; i < 30; i++)
            {
                scroll.ScrollToVerticalOffset(scroll.ScrollableHeight * i / 29);
                await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.Background);
            }
            PerformanceTrace.End("Scroll30Steps", phase);
            double scrollCpu = (process.TotalProcessorTime - cpu).TotalMilliseconds;
            int scrollLayouts = layouts;
            layouts = 0;
            phase = PerformanceTrace.Start();
            for (int i = 0; i < 20; i++)
            {
                window.Width = 1000 + (i % 5) * 70;
                window.UpdateLayout();
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            }
            PerformanceTrace.End("Resize20Steps", phase);
            window.LayoutUpdated -= handler;
            window.Width = 1200;
            scroll.ScrollToTop();
            window.UpdateLayout();
            Save(window, Path.Combine(root, "full.png"));
            long memory = GC.GetTotalMemory(true);
            var report = new
            {
                Count = content.Applications.Count, Timings = PerformanceTrace.Snapshot(),
                FirstCardCount = firstCount, CardCount = cards.Length,
                VisualsPerCard = cards.Length == 0 ? 0 : Descendants<DependencyObject>(cards[0]).Count(),
                ContextMenus = cards.Count(x => x.ContextMenu is not null),
                BitmapCount = content.Applications.Select(x => x.Icon).OfType<BitmapSource>().Distinct().Count(),
                ManagedBytes = memory, WorkingSet = process.WorkingSet64,
                GCCollections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray(),
                InteractionGC = Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - gcBefore[i]).ToArray(),
                ScrollLayouts = scrollLayouts, ResizeLayouts = layouts, ScrollCpuMs = scrollCpu
            };
            await File.WriteAllTextAsync(Path.Combine(root, "performance.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            PerformanceTrace.WriteSummary();
            window.Close();
        }
        catch (Exception ex)
        {
            PerformanceTrace.Log?.Write(LogLevel.Error, "PERF", "Session failed", ex);
            await File.WriteAllTextAsync(Path.Combine(ApplicationDataPaths.VerificationRoot!, "performance-error.txt"), ex.ToString());
            window.Close();
        }
    }

    private static void Save(Window window, string path)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
