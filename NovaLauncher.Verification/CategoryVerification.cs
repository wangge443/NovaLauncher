using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using NovaLauncher.App.Models;
using NovaLauncher.App.Persistence;
using NovaLauncher.App.Services;
using NovaLauncher.App.ViewModels;
using NovaLauncher.App.Views;

namespace NovaLauncher.Verification;

internal static partial class Program
{
    private static ICategoryService CategoryStore(ApplicationDataPaths paths) => new CategoryService(new CategoryRepository(new LauncherDbContextFactory(paths)));

    private static async Task RunCategoriesAsync(MainWindow window)
    {
        var oldPaths = new ApplicationDataPaths(Path.Combine(_fixtures, "old-5.0"));
        await SeedLegacyAsync(oldPaths, withHistory: true);
        string before = Rows(oldPaths.DatabasePath);
        await Initializer(oldPaths).InitializeAsync(default);
        Check(Rows(oldPaths.DatabasePath) == before, "5.0 upgrade preserves every existing application field");
        using (var db = new LauncherDbContextFactory(oldPaths).CreateDbContext())
        {
            Check(await db.Applications.AllAsync(x => x.CategoryId == null), "Existing applications start uncategorized");
            Check((await db.Database.GetAppliedMigrationsAsync()).SequenceEqual(db.Database.GetMigrations()) && !db.Database.HasPendingModelChanges(), "All migrations applied; snapshot matches model");
        }
        for (int index = 0; index < 3; index++) await Initializer(oldPaths).InitializeAsync(default);
        Check((await CategoryStore(oldPaths).GetAllAsync(default)).Select(x => x.Name).SequenceEqual(["游戏", "应用"]), "Default ordinary categories created exactly once");

        var paths = new ApplicationDataPaths(Path.Combine(_fixtures, "category-ui"));
        var store = CreatePersistence(paths);
        var categories = CategoryStore(paths);
        using var vm = new ContentViewModel(new ApplicationImportService(new ShortcutResolver(), new ApplicationIconService()),
            new ApplicationLaunchService(), store, new ToastService(), categories);
        var settingsService = SettingsService(paths); await vm.InitializeAsync(); await settingsService.LoadAsync();
        var shell = new MainWindowViewModel(vm, settings: new SettingsViewModel(settingsService, new SettingsDirectoryStub(), paths, vm.Toast));
        window.DataContext = shell;
        await vm.InitializeAsync();
        Check(shell.Sidebar.Categories.Count == 2 && shell.Sidebar.Items.Count == 3, "Sidebar uses loaded category collection");
        var manager = vm.CategoryManager;
        Check(!manager.CreateCommand.CanExecute(null), "Empty category cannot be submitted");
        Check((await categories.CreateAsync("  ", default)).Error is not null, "Whitespace name rejected by service");
        Check((await categories.CreateAsync(new string('长', 25), default)).Error is not null, "Overlong category rejected");
        window.Activate();
        var sidebar = Descendants<SidebarView>(window).Single();
        shell.Sidebar.OpenSettings(); window.UpdateLayout();
        manager.BeginCreateCommand.Execute(null);
        window.UpdateLayout();
        var editor = (TextBox)Descendants<CategoryManagementView>(window).Single().FindName("CategoryNameEditor");
        Check(editor.Focus(), "Inline editor accepts keyboard focus");
        editor.Text = "  开发工具  ";
        SaveWindow(window, "new-category.png");
        SendEditorKey(editor, Key.Enter);
        await WaitForAsync(() => !manager.IsEditing && manager.Categories.Count == 3);
        Check(manager.Categories.Last().Name == "开发工具" && shell.Sidebar.Items.Count == 4, "Enter trims, persists and immediately inserts sidebar category");
        Check((await categories.GetAllAsync(default)).Count == 3, "Category immediately saved in SQLite");
        manager.BeginCreateCommand.Execute(null);
        editor.Text = "开发工具";
        SendEditorKey(editor, Key.Enter);
        await WaitForAsync(() => !manager.CreateCommand.IsRunning && vm.Toast.IsOpen);
        Check(vm.Toast.Message == "该分类已存在" && manager.Categories.Count == 3 && manager.IsEditing, "Duplicate shows toast and preserves editor");
        await Task.Delay(200);
        SaveWindow(window, "duplicate-category.png");
        SendEditorKey(editor, Key.Escape);
        Check(!manager.IsEditing, "Escape cancels inline creation");
        await WaitForAsync(() => !vm.Toast.IsOpen);
        var repo = new CategoryRepository(new LauncherDbContextFactory(paths));
        bool duplicate = false;
        try { await repo.AddAsync(new Category { Id = Guid.NewGuid(), Name = "开发工具", CreatedAt = DateTimeOffset.UtcNow }, default); }
        catch (DbUpdateException) { duplicate = true; }
        Check(duplicate, "Unique index rejects duplicate bypassing service");

        string notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        string probe = CopyProbe("开发测试应用");
        await vm.ImportAsync([notepad, probe]);
        Check(vm.Applications.Count == 2 && vm.Applications.All(x => x.CategoryId == null), "New imports remain uncategorized");
        var first = vm.Applications[0];
        var game = manager.Categories.Single(x => x.Name == "游戏");
        var apps = manager.Categories.Single(x => x.Name == "应用");
        var tools = manager.Categories.Single(x => x.Name == "开发工具");
        await first.ChangeCategoryCommand.ExecuteAsync(new(game.Id, game.Name));
        Check(first.CategoryId == game.Id, "Assign application to Games");
        var navigation = Descendants<ListBox>(sidebar).First(x => x.ItemsSource == shell.Sidebar.Items);
        navigation.SelectedItem = game;
        Check(vm.SelectedCategory == game && vm.FilteredApplications.Cast<ApplicationCardViewModel>().SequenceEqual([first]), "Sidebar selection filters existing card instances");
        window.UpdateLayout();
        await Task.Delay(200);
        SaveWindow(window, "games-filter.png");
        await first.ChangeCategoryCommand.ExecuteAsync(new(tools.Id, tools.Name));
        Check(vm.FilteredApplications.IsEmpty && vm.Applications.Count == 2, "Moving out refreshes filter without destroying source collection");
        navigation.SelectedItem = tools;
        Check(vm.FilteredApplications.Cast<ApplicationCardViewModel>().Single() == first, "Custom category navigation finds moved application");
        navigation.SelectedItem = shell.Sidebar.Items[0];
        Check(vm.FilteredApplications.Cast<object>().Count() == 2, "Home shows all applications");
        var footer = Descendants<ListBox>(sidebar).First(x => x.ItemsSource == shell.Sidebar.FooterItems);
        shell.Sidebar.Profile.OpenSettingsCommand.Execute(null);
        Check(vm.Mode == WorkspaceMode.Settings && vm.SelectedCategory is null && vm.Applications.Count == 2, "Profile settings retains source applications");
        navigation.SelectedItem = game;
        Check(footer.SelectedItem is null && vm.SelectedCategory == game, "Top and footer navigation share a single selection");
        navigation.SelectedItem = shell.Sidebar.Items[0];

        window.UpdateLayout();
        var cardView = Descendants<ApplicationCardView>(window).First(x => ReferenceEquals(x.DataContext, first));
        await WaitForAsync(() => !vm.Toast.IsOpen);
        await OpenCardMenuAsync(cardView, true);
        var moveMenu = (MenuItem)cardView.FindName("MoveCategoryMenuItem");
        moveMenu.Focus(); ProfileKey(0x27); await WaitForAsync(() => moveMenu.IsSubmenuOpen);
        Check(moveMenu.Items.Count == 4, "Category submenu includes live custom category and uncategorized");
        CaptureCategoryWindow(window, "category-picker-desktop.png");
        var choice = moveMenu.Items.Cast<CategoryChoice>().Single(x => x.Id == apps.Id);
        await ActivateMenuAsync((MenuItem)moveMenu.ItemContainerGenerator.ContainerFromItem(choice));
        await WaitForAsync(() => first.CategoryId == apps.Id && !first.IsChangingCategory);
        Check(!cardView.ContextMenu.IsOpen && first.LaunchCount == 0, "Category menu saves and never launches application");
        await first.ChangeCategoryCommand.ExecuteAsync(new(null, "未分类"));
        Check(first.CategoryId is null, "Uncategorized clears nullable relation");
        await first.ChangeCategoryCommand.ExecuteAsync(new(tools.Id, tools.Name));
        await vm.FlushAsync();
        string restartRoot = Path.Combine(_fixtures, "category-ui");
        File.WriteAllLines(Path.Combine(restartRoot, "expected.txt"), [first.Application.Id.ToString(), tools.Id.ToString()]);
        await RunCategoryChildAsync("--category-restart", restartRoot);
        using (var restored = new ContentViewModel(new ApplicationImportService(new ShortcutResolver(), new ApplicationIconService()),
            new ApplicationLaunchService(), CreatePersistence(paths), new ToastService(), CategoryStore(paths)))
        {
            await restored.InitializeAsync();
            Check(restored.CategoryManager.Categories.Count == 3 && restored.Applications.Single(x => x.Application.Id == first.Application.Id).CategoryId == tools.Id,
                "Fresh service graph restores category records and application assignment");
        }
        string oldRows = Rows(paths.DatabasePath);
        await first.ChangeCategoryCommand.ExecuteAsync(new(Guid.NewGuid(), "无效分类"));
        Check(first.CategoryId == tools.Id && vm.Toast.Message == "分类修改失败" && Rows(paths.DatabasePath) == oldRows, "Invalid category fails with toast and preserves model/database");
        using (var connection = OpenDatabase(paths.DatabasePath))
        using (var transaction = connection.BeginTransaction(deferred: false))
        {
            await first.ChangeCategoryCommand.ExecuteAsync(new(game.Id, game.Name));
            Check(first.CategoryId == tools.Id && vm.Toast.Message == "分类修改失败", "Locked database does not optimistically move card");
            var failure = await categories.CreateAsync("无法写入", default);
            Check(failure.Error == "分类保存失败" && manager.Categories.Count == 3, "Category write failure has friendly result and no phantom item");
            transaction.Rollback();
        }
        using (var db = new LauncherDbContextFactory(paths).CreateDbContext())
        {
            await db.Categories.Where(x => x.Id == tools.Id).ExecuteDeleteAsync();
            Check((await db.Applications.SingleAsync(x => x.Id == first.Application.Id)).CategoryId is null, "Foreign key SetNull preserves application on future category deletion");
        }
        Check(Rows(paths.DatabasePath) == oldRows, "Category operations never modify original metadata or statistics");
        window.Width = 800;
        window.Height = 560;
        window.UpdateLayout();
        await Task.Delay(180);
        SaveWindow(window, "categories-small.png");
        Check(Descendants<ApplicationCardView>(window).All(x => x.ActualWidth == 136 && x.ActualHeight == 136), "Category controls preserve fixed card dimensions at minimum window size");
        window.Width = 1120;
        window.Height = 720;
        await vm.FlushAsync();
    }

    private static void SendEditorKey(UIElement editor, Key key) => editor.RaiseEvent(new KeyEventArgs(
        Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor), 0, key) { RoutedEvent = Keyboard.KeyDownEvent });

    private static void SaveElement(FrameworkElement element, string name)
    {
        element.UpdateLayout();
        var image = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(Path.Combine(_screenshots, name));
        encoder.Save(stream);
    }

    private static async Task RunCategoryChildAsync(string mode, string argument)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(mode);
        start.ArgumentList.Add(argument);
        using var child = Process.Start(start)!;
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));
        Check(child.ExitCode == 0, $"Independent WPF {mode}: {await stdout} {await stderr}");
    }

    private static int RunCategoryRestart(string root)
    {
        var paths = new ApplicationDataPaths(root);
        var app = new NovaLauncher.App.App(CreatePersistence(paths), CategoryStore(paths));
        app.InitializeComponent();
        app.Startup += (_, _) => app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                var shell = (MainWindowViewModel)app.MainWindow.DataContext;
                await shell.Content.InitializeAsync();
                var expected = File.ReadAllLines(Path.Combine(root, "expected.txt"));
                Check(shell.Sidebar.Categories.Count == 3, "Restart restores custom sidebar categories");
                var card = shell.Content.Applications.Single(x => x.Application.Id == Guid.Parse(expected[0]));
                Check(card.CategoryId == Guid.Parse(expected[1]) && card.Application.Icon is { Length: > 0 }, "Restart preserves category assignment and icon");
                shell.Sidebar.SelectedItem = shell.Sidebar.Categories.Single(x => x.Id == card.CategoryId);
                Check(shell.Content.FilteredApplications.Cast<ApplicationCardViewModel>().Single() == card, "Restarted custom navigation filters correctly");
                app.MainWindow.Close();
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); app.Shutdown(1); }
        }));
        return app.Run();
    }

    private sealed record CategoryMigrationSnapshot(string Rows, Dictionary<string, string> Icons);

    private static Dictionary<string, string> IconHashes(ApplicationDataPaths paths) => Directory.GetFiles(paths.IconsDirectory, "*.png")
        .ToDictionary(path => Path.GetFileName(path)!, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));

    private static async Task<int> VerifyRealCategoriesAsync()
    {
        var paths = ApplicationDataPaths.Default;
        var snapshot = new CategoryMigrationSnapshot(Rows(paths.DatabasePath), IconHashes(paths));
        string snapshotPath = Path.Combine(Path.GetTempPath(), "NovaLauncher-category-upgrade-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(snapshotPath, JsonSerializer.Serialize(snapshot));
        try
        {
            InspectDatabase();
            using (var db = new LauncherDbContextFactory(paths).CreateDbContext())
            {
                if ((await db.Database.GetPendingMigrationsAsync()).Any())
                {
                    Directory.CreateDirectory(paths.BackupsDirectory);
                    string backup = Path.Combine(paths.BackupsDirectory, $"nova-before-categories-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}.db");
                    using (new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
                    using var source = OpenDatabase(paths.DatabasePath, true);
                    using var destination = OpenDatabase(backup);
                    source.BackupDatabase(destination);
                    Check(Rows(backup) == snapshot.Rows, "Real pre-category backup matches original rows");
                    Console.WriteLine("Backup: " + backup);
                }
            }
            for (int index = 0; index < 2; index++) await RunCategoryChildAsync("--category-real-window", snapshotPath);
            Check(Rows(paths.DatabasePath) == snapshot.Rows, "Real QQ/WeChat metadata and launch statistics unchanged");
            Check(IconHashes(paths).OrderBy(x => x.Key).SequenceEqual(snapshot.Icons.OrderBy(x => x.Key)), "Real icon files are byte-for-byte unchanged");
            InspectDatabase();
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { File.Delete(snapshotPath); }
    }

    private static int RunRealCategoryWindow(string snapshotPath)
    {
        var snapshot = JsonSerializer.Deserialize<CategoryMigrationSnapshot>(File.ReadAllText(snapshotPath))!;
        var app = new NovaLauncher.App.App();
        app.InitializeComponent();
        _screenshots = Path.GetFullPath("docs/screenshots/5.1");
        Directory.CreateDirectory(_screenshots);
        app.Startup += (_, _) => app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                var shell = (MainWindowViewModel)app.MainWindow.DataContext;
                await shell.Content.InitializeAsync();
                Check(shell.Content.StatusMessage is null && shell.Content.Applications.Count >= 2, "Real window restores existing applications");
                Check(Rows(ApplicationDataPaths.Default.DatabasePath) == snapshot.Rows, "Real migration preserves all pre-category fields");
                Check(shell.Sidebar.Categories.Select(x => x.Name).SequenceEqual(["游戏", "应用"]), "Real default categories appear once");
                Check(shell.Content.Applications.All(x => x.CategoryId is null && x.Application.Icon is { Length: > 0 }), "Existing cards retain icons and are uncategorized");
                using var db = new LauncherDbContextFactory(ApplicationDataPaths.Default).CreateDbContext();
                Check((await db.Database.GetAppliedMigrationsAsync()).SequenceEqual(db.Database.GetMigrations()), "Real database applied both migrations");
                app.MainWindow.UpdateLayout();
                await Task.Delay(220);
                SaveWindow(app.MainWindow, "real-upgraded.png");
                app.MainWindow.Close();
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); app.Shutdown(1); }
        }));
        return app.Run();
    }

    private static void CaptureCategoryWindow(Window window, string filename)
    {
        window.Topmost = true;
        window.Activate();
        var hwnd = new WindowInteropHelper(window).Handle;
        CaptureNative.GetWindowRect(hwnd, out var rect);
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        var screen = CaptureNative.GetDC(IntPtr.Zero);
        var memory = CaptureNative.CreateCompatibleDC(screen);
        var bitmap = CaptureNative.CreateCompatibleBitmap(screen, width, height);
        var previous = CaptureNative.SelectObject(memory, bitmap);
        try
        {
            if (!CaptureNative.BitBlt(memory, 0, 0, width, height, screen, rect.Left, rect.Top, 0x40CC0020))
                throw new InvalidOperationException("Screen capture failed.");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions())));
            using var output = File.Create(Path.Combine(_screenshots, filename));
            encoder.Save(output);
        }
        finally
        {
            CaptureNative.SelectObject(memory, previous);
            CaptureNative.DeleteObject(bitmap);
            CaptureNative.DeleteDC(memory);
            CaptureNative.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private static class CaptureNative
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] internal static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("gdi32.dll")] internal static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] internal static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
        [DllImport("gdi32.dll")] internal static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] internal static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] internal static extern bool BitBlt(IntPtr dc, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, int rop);
    }
}
