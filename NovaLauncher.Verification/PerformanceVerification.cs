using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NovaLauncher.App.Models;
using NovaLauncher.App.Persistence;
using NovaLauncher.App.Services;
using NovaLauncher.App.ViewModels;
using NovaLauncher.App.Views;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Runtime.CompilerServices;
using BackgroundMode = NovaLauncher.App.Models.BackgroundMode;

namespace NovaLauncher.Verification;

internal static partial class Program
{
    private static async Task RunPerformanceBehaviorAsync(MainWindow window)
    {
        Guid category = Guid.NewGuid();
        var records = Enumerable.Range(0, 300).Select(i => new LauncherApplication(Guid.NewGuid(),
            $"应用 {i}", $@"C:\Performance\{i}.exe", $@"C:\Performance\{i}.exe", null, ApplicationSourceType.Executable)
        { CategoryId = i % 2 == 0 ? category : null, IsFavorite = i < 6,
            LastLaunchTime = i < 6 ? DateTimeOffset.UtcNow.AddMinutes(-i) : null }).ToArray();
        using var vm = new ContentViewModel(new ApplicationImportService(new ShortcutResolver(), new ApplicationIconService()),
            new ApplicationLaunchService(), new RecentMemoryStore(records), new ToastService());
        await vm.InitializeAsync();
        window.DataContext = new MainWindowViewModel(vm);
        window.UpdateLayout();
        var moved = vm.Applications[299];
        vm.Applications.Move(299, 0);
        var removed = vm.Applications[10];
        vm.Applications.Remove(removed);
        removed.Dispose();
        await WaitForAsync(() => Descendants<AllApplicationsView>(window).Single().IsFullyRendered);
        window.UpdateLayout();
        Check(AllCardViews(window).Length == 299 && AllCardViews(window)[0].DataContext == moved,
            "Move and removal during pending batches preserve final membership and order");
        var memory = new List<long>();
        var oldViews = new List<WeakReference>();
        var sidebarTimes = new List<double>();
        for (int cycle = 0; cycle < 5; cycle++)
        {
            oldViews.Add(LeavePerformanceHome(window, vm));
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            vm.SelectWorkspace(WorkspaceMode.Category, new(category, "性能分类"));
            window.UpdateLayout();
            Check(AllCardViews(window).Length == vm.Applications.Count(x => x.CategoryId == category), $"Cycle {cycle}: category membership correct");
            vm.SelectWorkspace(WorkspaceMode.Favorites);
            window.UpdateLayout();
            vm.SelectWorkspace(WorkspaceMode.Recent);
            window.UpdateLayout();
            vm.SelectWorkspace(WorkspaceMode.AllApplications);
            window.UpdateLayout();
            await WaitForAsync(() => Descendants<AllApplicationsView>(window).Single().IsFullyRendered);
            window.UpdateLayout();
            Check(Descendants<ApplicationCardView>(window).Count() == 299, $"Cycle {cycle}: Home has no duplicate card accumulation");
            var timer = Stopwatch.StartNew();
            var column = (ColumnDefinition)window.FindName("SidebarColumn");
            for (int step = 0; step < 20; step++)
            {
                column.Width = new GridLength(200 + step % 5 * 20);
                window.UpdateLayout();
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            }
            sidebarTimes.Add(timer.Elapsed.TotalMilliseconds);
            memory.Add(GC.GetTotalMemory(true));
        }
        await Task.Delay(300);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        int alive = oldViews.Count(x => x.IsAlive);
        Check(alive == 0, "Five unloaded Home views are collectible after dispatcher drains");
        Check(memory[^1] < memory[0] + 8 * 1024 * 1024, "Five full navigation cycles do not grow managed memory by more than 8 MiB");
        File.WriteAllText(Path.Combine(_screenshots, "behavior.json"), JsonSerializer.Serialize(new
        { ManagedBytes = memory, SidebarResize20StepsMs = sidebarTimes, OldAllApplicationsViewsAlive = alive }));
        SaveWindow(window, "home-299-navigation.png");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LeavePerformanceHome(MainWindow window, ContentViewModel vm)
    {
        var reference = new WeakReference(Descendants<AllApplicationsView>(window).Single());
        vm.SelectWorkspace(WorkspaceMode.Settings);
        window.UpdateLayout();
        return reference;
    }

    private static async Task<int> SetPerformanceBackgroundAsync(string root, int blur)
    {
        ApplicationDataPaths.ConfigureVerificationRoot(["--verification-data-root=" + Path.GetFullPath(root)]);
        var paths = ApplicationDataPaths.Default;
        string imagePath = Path.Combine(paths.BackgroundDirectory, "performance.png");
        if (blur >= 0 && !File.Exists(imagePath))
        {
            Directory.CreateDirectory(paths.BackgroundDirectory);
            byte[] pixels = new byte[1920 * 1080 * 4];
            for (int y = 0; y < 1080; y++)
                for (int x = 0; x < 1920; x++)
                {
                    int p = (y * 1920 + x) * 4;
                    pixels[p] = (byte)(160 + x % 90);
                    pixels[p + 1] = (byte)(120 + y % 130);
                    pixels[p + 2] = (byte)(100 + (x + y) % 150);
                    pixels[p + 3] = 255;
                }
            var bitmap = BitmapSource.Create(1920, 1080, 96, 96, PixelFormats.Bgra32, null, pixels, 1920 * 4);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(imagePath);
            png.Save(file);
        }
        var repository = new AppSettingsRepository(new LauncherDbContextFactory(paths));
        var original = await repository.GetOrCreateAsync();
        await repository.SaveAsync(original, original with
        {
            BackgroundMode = blur == -2 ? BackgroundMode.Transparent : blur < 0 ? BackgroundMode.Default : BackgroundMode.CustomImage,
            BackgroundImagePath = blur < 0 ? null : imagePath,
            BackgroundBlurRadius = Math.Max(0, blur)
        });
        return 0;
    }
    private static async Task<int> SeedPerformanceAsync(string root, int count)
    {
        if (count is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(count));
        if (Directory.Exists(root)) throw new IOException("Fixture must be new.");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, ".nova-verification"), "");
        ApplicationDataPaths.ConfigureVerificationRoot(["--verification-data-root=" + Path.GetFullPath(root)]);
        var paths = ApplicationDataPaths.Default;
        var factory = new LauncherDbContextFactory(paths);
        await new ApplicationRepository(factory, paths).InitializeAsync(default);
        var icons = new ApplicationIconService();
        byte[]? icon = icons.Extract(Path.Combine(Environment.SystemDirectory, "notepad.exe"));
        var cache = new ApplicationIconCache(paths, icons);
        await cache.InitializeAsync(default);
        using var db = factory.CreateDbContext();
        for (int i = 0; i < count; i++)
        {
            // Deterministic identity/order and cached PNGs; nonexistent targets cannot launch user programs.
            var id = new Guid(i + 1, 0, 0, new byte[8]);
            string target = Path.Combine(root, "Targets", $"{i:D3}.exe");
            db.Applications.Add(new ApplicationEntity
            {
                Id = id, DisplayName = $"应用 App {i:D3}", ExecutablePath = target,
                SourcePath = target, TargetPathKey = ApplicationRepository.PathKey(target), SourceType = 0,
                IsFavorite = i < 6, LaunchCount = i < 6 ? 1 : 0,
                LastLaunchTime = i < 6 ? DateTimeOffset.Parse("2026-10-01T00:00:00Z").AddMinutes(-i) : null,
                DateAdded = DateTimeOffset.Parse("2026-10-01T00:00:00Z"), SortOrder = i, IconCachePath = cache.PathFor(id)
            });
            await cache.WriteAsync(id, icon, default);
        }
        await db.SaveChangesAsync();
        Console.WriteLine($"Seeded {count} isolated applications: {root}");
        return 0;
    }
}
