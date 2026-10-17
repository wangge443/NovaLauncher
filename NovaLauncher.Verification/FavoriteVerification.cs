using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NovaLauncher.App.Persistence;
using NovaLauncher.App.Services;
using NovaLauncher.App.ViewModels;
using NovaLauncher.App.Views;

namespace NovaLauncher.Verification;

internal static partial class Program
{
    private static string PreFavoriteRows(string path)
    {
        using var db = OpenDatabase(path, true);
        return Rows(path) + Query(db, "SELECT Id, CategoryId FROM Applications ORDER BY Id;") +
            Query(db, "SELECT Id, Name, CreatedAt, SortOrder FROM Categories ORDER BY Id;");
    }

    private static async Task RunFavoritesAsync(MainWindow window)
    {
        var old = new ApplicationDataPaths(Path.Combine(_fixtures, "old-5.1"));
        await SeedLegacyAsync(old, true);
        using (var db = new LauncherDbContextFactory(old).CreateDbContext())
        {
            await db.GetService<IMigrator>().MigrateAsync("20261016043853_AddApplicationCategories");
            await db.Database.ExecuteSqlRawAsync("UPDATE Applications SET CategoryId = (SELECT Id FROM Categories WHERE Name = '游戏');");
        }
        string before = PreFavoriteRows(old.DatabasePath);
        await Initializer(old).InitializeAsync(default);
        Check(PreFavoriteRows(old.DatabasePath) == before, "5.1 migration preserves all application fields, assignments and category rows");
        using (var db = new LauncherDbContextFactory(old).CreateDbContext())
        {
            Check(await db.Applications.AllAsync(x => !x.IsFavorite), "Every old application defaults to false");
            Check(!db.Database.HasPendingModelChanges() && !(await db.Database.GetPendingMigrationsAsync()).Any(), "Favorite migration and model snapshot match");
        }
        using (var db = OpenDatabase(old.DatabasePath, true))
            Check(Query(db, "SELECT type, [notnull], dflt_value FROM pragma_table_info('Applications') WHERE name='IsFavorite';") == "[[\"INTEGER\",1,\"0\"]]", "IsFavorite is INTEGER NOT NULL DEFAULT 0");

        var paths = new ApplicationDataPaths(Path.Combine(_fixtures, "favorites"));
        using var vm = new ContentViewModel(new ApplicationImportService(new ShortcutResolver(), new ApplicationIconService()),
            new ApplicationLaunchService(), CreatePersistence(paths), new ToastService(), CategoryStore(paths));
        var shell = new MainWindowViewModel(vm);
        window.DataContext = shell;
        await vm.InitializeAsync();
        await vm.ImportAsync([Path.Combine(Environment.SystemDirectory, "notepad.exe"), CopyProbe("收藏测试")]);
        var first = vm.Applications[0];
        Check(vm.Applications.All(x => !x.IsFavorite), "New imports default to not favorite");
        var game = shell.Sidebar.Categories.First();
        await first.ChangeCategoryCommand.ExecuteAsync(new(game.Id, game.Name));
        string initialRows = PreFavoriteRows(paths.DatabasePath);
        window.Topmost = true;
        LaunchNative.ShowWindow(new WindowInteropHelper(window).Handle, 1);
        window.Activate();
        LaunchNative.SetForegroundWindow(new WindowInteropHelper(window).Handle);
        window.UpdateLayout();
        await Task.Delay(220);
        var view = Descendants<ApplicationCardView>(window).Single(x => x.DataContext == first);
        var favorite = (MenuItem)view.FindName("FavoriteMenuItem");
        await OpenCardMenuAsync(view);
        Check(AutomationProperties.GetName(favorite) == "收藏 " + first.DisplayName, "Favorite menu has dynamic accessible name");
        await ActivateMenuAsync(favorite);
        await WaitForAsync(() => first.IsFavorite && !first.IsSavingFavorite);
        Check(first.LaunchCount == 0 && first.LastLaunchTime is null, "Menu favorites without launching application");
        Check(first.CategoryId == game.Id && PreFavoriteRows(paths.DatabasePath) == initialRows, "Favorite preserves category metadata and statistics");
        await OpenCardMenuAsync(view);
        Check(!vm.Toast.IsOpen && AutomationProperties.GetName(favorite) == "取消收藏 " + first.DisplayName, "Success silent and accessible action updated");
        await ActivateMenuAsync(favorite);
        await WaitForAsync(() => !first.IsFavorite && !first.IsSavingFavorite);
        Check(first.LaunchCount == 0, "Menu unfavorites without launching");
        await OpenCardMenuAsync(view, true);
        Check(view.ContextMenu.IsOpen, "Shift F10 reaches favorite action");
        await ActivateMenuAsync(favorite); await WaitForAsync(() => first.IsFavorite && !first.IsSavingFavorite);
        Check(first.LaunchCount == 0, "Enter toggles menu favorite only");
        await OpenCardMenuAsync(view, true); await ActivateMenuAsync(favorite);
        await WaitForAsync(() => !first.IsFavorite && !first.IsSavingFavorite);
        Check(first.LaunchCount == 0, "Keyboard unfavorite does not launch");
        var quiet = window.PointToScreen(new Point(window.ActualWidth - 80, 150));
        await first.ToggleFavoriteCommand.ExecuteAsync(null);

        var sidebar = Descendants<SidebarView>(window).Single();
        var footer = Descendants<ListBox>(sidebar).Single(x => x.ItemsSource == shell.Sidebar.FooterItems);
        footer.SelectedItem = shell.Sidebar.FooterItems[0];
        Check(vm.Mode == WorkspaceMode.Favorites && vm.SelectedCategory is null && vm.FilteredApplications.Cast<object>().SequenceEqual([first]), "Favorites Sidebar filters existing cards with one active mode");
        window.UpdateLayout();
        Native.SetCursorPos((int)quiet.X, (int)quiet.Y);
        Keyboard.ClearFocus();
        await Task.Delay(220);
        SaveWindow(window, "favorites-page.png");
        await first.ToggleFavoriteCommand.ExecuteAsync(null);
        Check(vm.IsEmpty && vm.Applications.Count == 2, "Unfavorite disappears immediately from Favorites but retains source collection");
        Check(vm.EmptyTitle == "还没有收藏应用" && vm.EmptyHint == "右键应用卡片即可收藏", "Favorites has dedicated empty state");
        window.UpdateLayout();
        SaveWindow(window, "favorites-empty.png");
        await vm.ImportAsync([CopyProbe("未自动收藏")]);
        Check(vm.Applications.Count == 3 && vm.IsEmpty && !vm.Applications.Last().IsFavorite, "Favorites import remains unfavorited and hidden");
        shell.Sidebar.SelectedItem = shell.Sidebar.Items[0];
        Check(vm.Mode == WorkspaceMode.Home && vm.FilteredApplications.Cast<object>().Count() == 3, "Home restores all applications");
        shell.Sidebar.SelectedItem = game;
        await first.ToggleFavoriteCommand.ExecuteAsync(null);
        Check(vm.Mode == WorkspaceMode.Category && first.CategoryId == game.Id && vm.FilteredApplications.Cast<object>().SequenceEqual([first]), "Favoriting within Category keeps its filter and assignment");
        await first.ChangeCategoryCommand.ExecuteAsync(new(null, "未分类"));
        Check(first.IsFavorite && vm.IsEmpty, "Category change preserves favorite and refreshes category filter");
        await first.ChangeCategoryCommand.ExecuteAsync(new(game.Id, game.Name));
        using (var connection = OpenDatabase(paths.DatabasePath))
        using (var transaction = connection.BeginTransaction(deferred: false))
        {
            Task saving = first.ToggleFavoriteCommand.ExecuteAsync(null);
            await Task.Delay(50);
            Check(first.IsFavorite && first.IsSavingFavorite, "Pending write retains last committed favorite state");
            await saving;
            Check(first.IsFavorite && vm.Toast.Message == "收藏状态保存失败", "Locked SQLite failure preserves favorite and shows error toast");
            transaction.Rollback();
        }
        using (var db = new LauncherDbContextFactory(paths).CreateDbContext())
            Check((await db.Applications.SingleAsync(x => x.Id == first.Application.Id)).IsFavorite, "Failed write leaves database favorite unchanged");
        await first.ToggleFavoriteCommand.ExecuteAsync(null);
        Check(!first.IsFavorite, "Favorite command recovers after write failure");
        await first.ToggleFavoriteCommand.ExecuteAsync(null);
        Check(first.CategoryId == game.Id && first.LaunchCount == 0 && first.LastLaunchTime is null, "Favorite recovery preserves category and statistics");
        await vm.FlushAsync();
        var snapshot = new FavoriteSnapshot(Path.GetDirectoryName(Path.GetDirectoryName(paths.DatabasePath)!)!, PreFavoriteRows(paths.DatabasePath), IconHashes(paths), first.Application.Id);
        string snapshotFile = Path.Combine(_fixtures, "favorite-restart.json");
        File.WriteAllText(snapshotFile, JsonSerializer.Serialize(snapshot));
        await RunCategoryChildAsync("--favorite-restart", snapshotFile);
        await WaitForAsync(() => !vm.Toast.IsOpen);
        window.Width = 800;
        window.Height = 560;
        window.UpdateLayout();
        Check(Descendants<ApplicationCardView>(window).All(x => x.ActualWidth == 136 && x.ActualHeight == 136), "Favorites controls preserve card dimensions at minimum window");
        SaveWindow(window, "favorites-small.png");
    }

    private static async Task ClickFavoriteElementAsync(Window window, FrameworkElement element)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        LaunchNative.ShowWindow(hwnd, 1);
        LaunchNative.SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x13);
        window.Activate();
        LaunchNative.SetForegroundWindow(hwnd);
        await Task.Delay(300);
        var point = element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        Native.SetCursorPos((int)point.X, (int)point.Y);
        await Task.Delay(220);
        if (LaunchNative.WindowFromPoint(new((int)point.X, (int)point.Y)) != new WindowInteropHelper(window).Handle)
            {
            var found = LaunchNative.WindowFromPoint(new((int)point.X, (int)point.Y));
            ProfileDialogNative.GetWindowThreadProcessId(found, out uint process);
            CaptureCategoryWindow(window, "obscured.png");
            throw new InvalidOperationException($"Verification window is obscured; refusing to click another window. Expected HWND={hwnd}, actual={found}, PID={process}, point={point}");
        }
        LaunchNative.mouse_event(2, 0, 0, 0, UIntPtr.Zero);
        LaunchNative.mouse_event(4, 0, 0, 0, UIntPtr.Zero);
    }

    private sealed record FavoriteSnapshot(string Root, string Rows, Dictionary<string, string> Icons, Guid FavoriteId);

    private static async Task<int> VerifyRealFavoritesAsync()
    {
        var paths = ApplicationDataPaths.Default;
        string snapshotPath = Path.Combine(Path.GetTempPath(), "NovaLauncher-favorites-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            using var source = OpenDatabase(paths.DatabasePath, true);
            using var command = source.CreateCommand();
            command.CommandText = "SELECT Id FROM Applications ORDER BY CASE WHEN DisplayName='QQ' THEN 0 ELSE 1 END, Id LIMIT 1;";
            Guid id = Guid.Parse((string)command.ExecuteScalar()!);
            var snapshot = new FavoriteSnapshot(Path.GetDirectoryName(Path.GetDirectoryName(paths.DatabasePath)!)!, PreFavoriteRows(paths.DatabasePath), IconHashes(paths), id);
            File.WriteAllText(snapshotPath, JsonSerializer.Serialize(snapshot));
            Directory.CreateDirectory(paths.BackupsDirectory);
            string backup = Path.Combine(paths.BackupsDirectory, $"nova-before-favorites-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}.db");
            using (new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            using (var destination = OpenDatabase(backup)) source.BackupDatabase(destination);
            Check(PreFavoriteRows(backup) == snapshot.Rows, "Real online backup preserves all pre-favorite data");
            Console.WriteLine("Backup: " + backup);
            Console.WriteLine("Before: " + Query(source, "SELECT DisplayName, CategoryId, LaunchCount, LastLaunchTime FROM Applications ORDER BY Id;"));
            await RunCategoryChildAsync("--favorite-real-set", snapshotPath);
            await RunCategoryChildAsync("--favorite-real-check", snapshotPath);
            Check(PreFavoriteRows(paths.DatabasePath) == snapshot.Rows, "Real applications, categories, counts and timestamps unchanged");
            Check(IconHashes(paths).OrderBy(x => x.Key).SequenceEqual(snapshot.Icons.OrderBy(x => x.Key)), "Real PNG cache hashes unchanged");
            Console.WriteLine("After: " + Query(source, "SELECT DisplayName, CategoryId, LaunchCount, LastLaunchTime, IsFavorite FROM Applications ORDER BY Id;"));
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { File.Delete(snapshotPath); }
    }

    private static int RunFavoriteWindow(string snapshotFile, bool real, bool setFavorite)
    {
        var snapshot = JsonSerializer.Deserialize<FavoriteSnapshot>(File.ReadAllText(snapshotFile))!;
        var paths = new ApplicationDataPaths(snapshot.Root);
        var app = real ? new NovaLauncher.App.App() : new NovaLauncher.App.App(CreatePersistence(paths), CategoryStore(paths));
        app.InitializeComponent();
        app.Startup += (_, _) => app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                var shell = (MainWindowViewModel)app.MainWindow.DataContext;
                await shell.Content.InitializeAsync();
                Check(shell.Content.StatusMessage is null && PreFavoriteRows(paths.DatabasePath) == snapshot.Rows, "Window startup preserves every pre-favorite field");
                var card = shell.Content.Applications.Single(x => x.Application.Id == snapshot.FavoriteId);
                if (setFavorite)
                {
                    Check(shell.Content.Applications.All(x => !x.IsFavorite), "Real migrated applications all default false");
                    await card.ToggleFavoriteCommand.ExecuteAsync(null);
                }
                Check(card.IsFavorite && card.FavoriteGlyph == "\uE735", "Favorite state and filled star restored");
                shell.Sidebar.SelectedItem = shell.Sidebar.FooterItems[0];
                Check(shell.Content.FilteredApplications.Cast<ApplicationCardViewModel>().Contains(card), "Restarted Favorites page contains selected application");
                using var db = new LauncherDbContextFactory(paths).CreateDbContext();
                Check(!(await db.Database.GetPendingMigrationsAsync()).Any() && !db.Database.HasPendingModelChanges(), "All migrations applied and snapshot matches");
                if (real)
                {
                    _screenshots = Path.GetFullPath("docs/screenshots/5.2");
                    Directory.CreateDirectory(_screenshots);
                    app.MainWindow.UpdateLayout();
                    await Task.Delay(220);
                    SaveWindow(app.MainWindow, setFavorite ? "real-favorite.png" : "real-favorite-restarted.png");
                }
                app.MainWindow.Close();
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); app.Shutdown(1); }
        }));
        return app.Run();
    }
}
