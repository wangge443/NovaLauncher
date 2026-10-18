using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.EntityFrameworkCore;
using NovaLauncher.App.Persistence;
using NovaLauncher.App.Services;
using NovaLauncher.App.ViewModels;
using NovaLauncher.App.Views;

namespace NovaLauncher.Verification;

internal static partial class Program
{
    private sealed class LocationProbe : IApplicationLocationService
    {
        public string? Path { get; private set; }
        public Task<string?> OpenAsync(string path) { Path = path; return Task.FromResult<string?>(File.Exists(path) ? null : "应用文件不存在"); }
    }

    private static async Task OpenCardMenuAsync(ApplicationCardView card, bool keyboard = false)
    {
        var window = Window.GetWindow(card);
        window.Topmost = true; window.Activate();
        LaunchNative.SetForegroundWindow(new WindowInteropHelper(window).Handle);
        card.BringIntoView(); window.UpdateLayout(); await Task.Delay(220);
        if (keyboard)
        {
            ((Button)card.FindName("LaunchButton")).Focus();
            LaunchNative.keybd_event(0x10, 0, 0, UIntPtr.Zero);
            ProfileKey(0x79);
            LaunchNative.keybd_event(0x10, 0, 2, UIntPtr.Zero);
        }
        else
        {
            var point = card.PointToScreen(new Point(65, 65));
            Native.SetCursorPos((int)point.X, (int)point.Y); await Task.Delay(180);
            var hitWindow = LaunchNative.WindowFromPoint(new((int)point.X, (int)point.Y));
            if (hitWindow != new WindowInteropHelper(window).Handle)
            {
                ProfileDialogNative.GetWindowThreadProcessId(hitWindow, out uint process);
                SaveWindow(window, "obscured-card.png");
                throw new InvalidOperationException($"Card is obscured; refusing native right click. Point={point}; hit HWND={hitWindow}; PID={process}; test HWND={new WindowInteropHelper(window).Handle}; card size={card.ActualWidth}x{card.ActualHeight}");
            }
            LaunchNative.mouse_event(8, 0, 0, 0, UIntPtr.Zero);
            LaunchNative.mouse_event(16, 0, 0, 0, UIntPtr.Zero);
        }
        await WaitForAsync(() => card.ContextMenu.IsOpen);
        await Task.Delay(180);
    }

    private static async Task ActivateMenuAsync(MenuItem item)
    {
        Check(item.Focus(), "Menu item accepts keyboard focus");
        ProfileKey(0x0D); await Task.Delay(180);
    }

    private static async Task MoveWithMenuAsync(ApplicationCardView card, Guid? category)
    {
        await OpenCardMenuAsync(card, true);
        var move = (MenuItem)card.FindName("MoveCategoryMenuItem");
        move.Focus(); ProfileKey(0x27);
        await WaitForAsync(() => move.IsSubmenuOpen);
        var choice = move.Items.Cast<CategoryChoice>().Single(x => x.Id == category);
        var item = (MenuItem)move.ItemContainerGenerator.ContainerFromItem(choice);
        await ActivateMenuAsync(item);
    }

    private static string CompleteDataState(ApplicationDataPaths paths)
    {
        using var db = OpenDatabase(paths.DatabasePath, true);
        var backgrounds = Directory.Exists(paths.BackgroundDirectory) ? Directory.GetFiles(paths.BackgroundDirectory).OrderBy(x => x)
            .Select(x => Path.GetFileName(x) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x)))).ToArray() : [];
        return JsonSerializer.Serialize(new { old = SettingsExistingState(paths), settings = Query(db, "SELECT * FROM AppSettings ORDER BY Id"), backgrounds,
            schema = Query(db, "SELECT name, sql FROM sqlite_master WHERE type='table' ORDER BY name") });
    }

    private static async Task RunManagementAsync(MainWindow window)
    {
        var real = ApplicationDataPaths.Default;
        string realBefore = CompleteDataState(real);
        var paths = new ApplicationDataPaths(Path.Combine(_fixtures, "management"));
        var location = new LocationProbe();
        using var content = new ContentViewModel(new ApplicationImportService(new ShortcutResolver(), new ApplicationIconService()),
            new ApplicationLaunchService(), CreatePersistence(paths), new ToastService(), CategoryStore(paths), location);
        await content.InitializeAsync();
        var settingsService = SettingsService(paths); await settingsService.LoadAsync();
        var settings = new SettingsViewModel(settingsService, new SettingsDirectoryStub(), paths, content.Toast);
        var shell = new MainWindowViewModel(content, settings: settings);
        window.DataContext = shell; window.Topmost = true; window.Activate(); window.UpdateLayout();
        var sidebar = Descendants<SidebarView>(window).Single();
        Check(!Descendants<Button>(sidebar).Any(x => AutomationProperties.GetAutomationId(x) == "NewCategory"), "Sidebar has no create category action");
        Check(shell.Sidebar.Categories.Count == 2, "Sidebar preserves existing dynamic categories");
        shell.Sidebar.OpenSettings(); window.UpdateLayout();
        var management = Descendants<CategoryManagementView>(window).Single();
        Check(ReferenceEquals(management.DataContext, content.CategoryManager), "Settings shares existing CategoryManager and services");
        var create = (Button)management.FindName("NewCategoryButton"); create.BringIntoView(); window.UpdateLayout();
        await ClickFavoriteElementAsync(window, create);
        var editor = (TextBox)management.FindName("CategoryNameEditor");
        await WaitForAsync(() => editor.IsKeyboardFocused);
        editor.Text = "  开发工具  "; ProfileKey(0x0D);
        await WaitForAsync(() => !content.CategoryManager.IsEditing);
        Check(content.CategoryManager.Categories.Last().Name == "开发工具" && shell.Sidebar.Items.Count == 4, "Settings Enter trims and immediately updates Sidebar");
        SaveWindow(window, "settings-categories.png");
        Check((await CategoryStore(paths).GetAllAsync(default)).Count == 3, "Settings category saved to SQLite");
        content.CategoryManager.BeginCreateCommand.Execute(null); editor.Text = " ";
        Check(!content.CategoryManager.CreateCommand.CanExecute(null), "Empty category rejected");
        editor.Text = new string('长', 25); Check(!content.CategoryManager.CreateCommand.CanExecute(null), "Overlong category rejected");
        editor.Text = "开发工具"; await content.CategoryManager.CreateCommand.ExecuteAsync(null);
        Check(content.Toast.Message == "该分类已存在", "Duplicate category uses existing toast");
        editor.Focus(); ProfileKey(0x1B); await Task.Delay(100);
        Check(!content.CategoryManager.IsEditing, "Escape cancels Settings inline editor");
        await WaitForAsync(() => !content.Toast.IsOpen);
        await content.ImportAsync([CopyProbe("菜单测试"), Path.Combine(Environment.SystemDirectory, "notepad.exe")]);
        shell.Sidebar.SelectedItem = shell.Sidebar.Items[0]; window.UpdateLayout(); await Task.Delay(200);
        var first = content.Applications[0]; var card = AllCardViews(window).Single(x => x.DataContext == first);
        Check(AllCardViews(window).All(x => x.FindName("FavoriteButton") is null && x.FindName("CategoryButton") is null), "Shared card has no visible star or duplicate category button");
        Keyboard.ClearFocus(); var quiet = window.PointToScreen(new Point(700, 90)); Native.SetCursorPos((int)quiet.X, (int)quiet.Y); await Task.Delay(220);
        SaveWindow(window, "sidebar.png"); SaveWindow(window, "card-normal.png");
        await OpenCardMenuAsync(card);
        Check(first.LaunchCount == 0, "Native right click never launches");
        Check(card.ContextMenu.Items.Count == 11, "Context menu retains original actions alongside editing actions and separators");
        CaptureCategoryWindow(window, "context-menu.png");
        var favorite = (MenuItem)card.FindName("FavoriteMenuItem");
        Check((string)favorite.Header == "收藏", "Menu initially offers favorite");
        var menuPoint=favorite.PointToScreen(new Point(favorite.ActualWidth/2,favorite.ActualHeight/2));
        var menuHwnd=((HwndSource)PresentationSource.FromVisual(favorite)).Handle;
        Native.SetCursorPos((int)menuPoint.X,(int)menuPoint.Y); await Task.Delay(150);
        if(LaunchNative.WindowFromPoint(new((int)menuPoint.X,(int)menuPoint.Y))!=menuHwnd) throw new InvalidOperationException("Menu obscured; refusing click");
        LaunchNative.mouse_event(2,0,0,0,UIntPtr.Zero); LaunchNative.mouse_event(4,0,0,0,UIntPtr.Zero);
        await WaitForAsync(() => first.IsFavorite);
        Check(content.Home.FavoriteApplications.Contains(first) && first.LaunchCount == 0, "Menu favorite updates Home without launch");
        await OpenCardMenuAsync(card, true);
        Check((string)favorite.Header == "取消收藏", "Shift F10 opens menu with updated favorite label");
        ProfileKey(0x28); ProfileKey(0x1B); await Task.Delay(150);
        Check(!card.ContextMenu.IsOpen && first.LaunchCount == 0, "Menu arrows and Escape do not launch");
        ((Button)card.FindName("LaunchButton")).Focus(); ProfileKey(0x5D); await WaitForAsync(()=>card.ContextMenu.IsOpen);
        Check(card.ContextMenu.IsOpen,"Context Menu key opens application actions"); ProfileKey(0x1B); await Task.Delay(100);
        await OpenCardMenuAsync(card);
        var move = (MenuItem)card.FindName("MoveCategoryMenuItem"); move.Focus(); ProfileKey(0x27);
        await WaitForAsync(() => move.IsSubmenuOpen);
        Check(move.Items.Count == 4, "Category submenu includes uncategorized and live custom categories");
        CaptureCategoryWindow(window, "category-submenu.png");
        var custom = content.CategoryManager.Categories.Last();
        await ActivateMenuAsync((MenuItem)move.ItemContainerGenerator.ContainerFromItem(move.Items.Cast<CategoryChoice>().Single(x => x.Id == custom.Id)));
        await WaitForAsync(() => first.CategoryId == custom.Id);
        Check(first.LaunchCount == 0, "Category menu saves without launching");
        shell.Sidebar.SelectedItem = custom; window.UpdateLayout();
        Check(content.FilteredApplications.Cast<object>().Single() == first, "Category page reflects menu move");
        card = Descendants<ApplicationCardView>(window).Single();
        await MoveWithMenuAsync(card, null); await WaitForAsync(() => content.IsEmpty);
        Check(first.CategoryId is null, "Moving to uncategorized removes card from current category");
        shell.Sidebar.SelectedItem = shell.Sidebar.FooterItems[0]; window.UpdateLayout();
        SaveWindow(window, "favorites.png"); card = Descendants<ApplicationCardView>(window).Single();
        await OpenCardMenuAsync(card); await ActivateMenuAsync((MenuItem)card.FindName("FavoriteMenuItem")); await WaitForAsync(() => content.IsEmpty);
        Check(!content.Home.FavoriteApplications.Contains(first), "Unfavorite removes Favorites card and Home projection");
        Check(content.EmptyHint == "右键应用卡片即可收藏", "Favorites empty state no longer mentions star"); SaveWindow(window, "favorites-empty.png");
        shell.Sidebar.SelectedItem = shell.Sidebar.Items[0]; window.UpdateLayout(); card = AllCardViews(window).Single(x => x.DataContext == first);
        await OpenCardMenuAsync(card); await ActivateMenuAsync((MenuItem)card.FindName("InfoMenuItem"));
        var info = (Popup)card.FindName("InfoPopup"); await WaitForAsync(() => info.IsOpen);
        Check(Descendants<TextBlock>(info.Child).Any(x => x.Text == first.DisplayName) && Descendants<TextBox>(info.Child).Single().Text == first.TargetPath, "Read-only information shows name and target path");
        Check(first.SourceTypeText == "EXE" && first.CategoryName == "未分类" && first.FavoriteStatus == "未收藏", "Information presents type category and favorite state");
        CaptureCategoryWindow(window, "application-info.png"); ProfileKey(0x1B); await WaitForAsync(() => !info.IsOpen);
        Check(first.LaunchCount == 0, "Information opens and closes without launch");
        await OpenCardMenuAsync(card); await ActivateMenuAsync((MenuItem)card.FindName("LocationMenuItem"));
        Check(location.Path == first.TargetPath && first.LaunchCount == 0, "File location menu uses target path without launch");
        await ClickFavoriteElementAsync(window, (Button)card.FindName("LaunchButton")); await WaitForAsync(() => first.LaunchCount == 1 && !first.IsLaunching);
        Check(first.LaunchCount == 1, "Left click still launches");
        window.Activate(); LaunchNative.SetForegroundWindow(new WindowInteropHelper(window).Handle); await Task.Delay(200);
        card=AllCardViews(window).Single(x=>x.DataContext==first);
        ((Button)card.FindName("LaunchButton")).Focus(); ProfileKey(0x0D); await WaitForAsync(() => first.LaunchCount == 2 && !first.IsLaunching);
        window.Activate(); LaunchNative.SetForegroundWindow(new WindowInteropHelper(window).Handle); await Task.Delay(200);
        ((Button)card.FindName("LaunchButton")).Focus(); ProfileKey(0x20); await WaitForAsync(() => first.LaunchCount == 3 && !first.IsLaunching);
        Check(first.LaunchCount == 3, "Enter and Space still launch");
        var hit = card.PointToScreen(new Point(65,65)); Native.SetCursorPos((int)hit.X,(int)hit.Y); await Task.Delay(220); SaveWindow(window,"card-hover.png");
        shell.Sidebar.SelectedItem = shell.Sidebar.FooterItems[1]; window.UpdateLayout(); Check(content.FilteredApplications.Cast<object>().Contains(first), "Recent still displays launched application");
        shell.Sidebar.SelectedItem = shell.Sidebar.Items[0]; window.UpdateLayout();
        window.Width=800; window.Height=560; ((ColumnDefinition)window.FindName("SidebarColumn")).Width=new GridLength(280); window.UpdateLayout();
        card=AllCardViews(window).Single(x=>x.DataContext==first); await OpenCardMenuAsync(card,true);
        var work=SystemParameters.WorkArea; var corner=card.ContextMenu.PointToScreen(new Point(card.ContextMenu.ActualWidth,card.ContextMenu.ActualHeight));
        var scale=PresentationSource.FromVisual(window)!.CompositionTarget.TransformToDevice;
        Check(corner.X<=work.Right*scale.M11+2 && corner.Y<=work.Bottom*scale.M22+2,"Menu stays within screen work area at small window");
        card.ContextMenu.IsOpen=false; window.WindowState=WindowState.Maximized; await Task.Delay(180);
        await OpenCardMenuAsync(card,true); Check(card.ContextMenu.IsOpen,"Menu opens after maximize and Sidebar resize"); card.ContextMenu.IsOpen=false; window.WindowState=WindowState.Normal;
        // Force the same WPF popup placement mechanism against the bottom-right work-area boundary.
        var menu=card.ContextMenu; menu.PlacementTarget=card; menu.Placement=PlacementMode.AbsolutePoint;
        menu.HorizontalOffset=work.Right-4; menu.VerticalOffset=work.Bottom-4; menu.IsOpen=true; await Task.Delay(200);
        var top=menu.PointToScreen(new Point()); var bottom=menu.PointToScreen(new Point(menu.ActualWidth,menu.ActualHeight));
        Check(top.X>=work.Left*scale.M11-2 && top.Y>=work.Top*scale.M22-2 && bottom.X<=work.Right*scale.M11+2 && bottom.Y<=work.Bottom*scale.M22+2,"Bottom-right menu repositions fully into work area");
        var edgeMove=(MenuItem)card.FindName("MoveCategoryMenuItem"); edgeMove.IsSubmenuOpen=true; await Task.Delay(200);
        var edgePopup=(Popup)edgeMove.Template.FindName("PART_Popup",edgeMove); var edgeChild=(FrameworkElement)edgePopup.Child;
        var edgeBottom=edgeChild.PointToScreen(new Point(edgeChild.ActualWidth,edgeChild.ActualHeight));
        Check(edgeBottom.X<=work.Right*scale.M11+2 && edgeBottom.Y<=work.Bottom*scale.M22+2,"Category submenu also stays in work area");
        menu.IsOpen=false; menu.HorizontalOffset=0; menu.VerticalOffset=0;
        var missing=new ApplicationLocationService(); Check(await missing.OpenAsync(Path.Combine(_fixtures,"missing.exe"))=="应用文件不存在","Missing file reports safe error");
        using(var missingCard=new ApplicationCardViewModel(first.Application with {ExecutablePath=Path.Combine(_fixtures,"missing.exe")},new ApplicationLaunchService(),new MemoryPersistence(),content.CategoryManager,content.Toast,missing))
        {
            await missingCard.OpenLocationCommand.ExecuteAsync(null);
            Check(content.Toast.Message=="应用文件不存在","Missing file command shows required toast");
        }
        using(var shortcut=new ApplicationCardViewModel(first.Application with {SourceType=NovaLauncher.App.Models.ApplicationSourceType.Shortcut,SourcePath="source.lnk"},new ApplicationLaunchService(),new MemoryPersistence(),content.CategoryManager,content.Toast,location))
        {
            await shortcut.OpenLocationCommand.ExecuteAsync(null);
            Check(shortcut.SourceTypeText=="Shortcut" && location.Path==first.TargetPath,"Shortcut location resolves target EXE rather than source link");
        }
        await VerifyExplorerLocationAsync(first.TargetPath);
        using(var db=new LauncherDbContextFactory(paths).CreateDbContext()) Check(!db.Database.HasPendingModelChanges() && !(await db.Database.GetPendingMigrationsAsync()).Any(),"Schema unchanged and migrations match");
        Check(CompleteDataState(real)==realBefore,"Real applications categories favorites stats profile settings background and schema unchanged");
    }

    private static async Task VerifyExplorerLocationAsync(string target)
    {
        dynamic shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        var before=new HashSet<int>(); foreach(dynamic entry in shell.Windows()) before.Add((int)entry.HWND);
        try
        {
            Check(await new ApplicationLocationService().OpenAsync(target) is null,"Windows Shell accepts file selection");
            bool selected=false;
            for(int attempt=0;attempt<40 && !selected;attempt++)
            {
                await Task.Delay(100);
                foreach(dynamic entry in shell.Windows())
                {
                    if(!Uri.TryCreate((string)entry.LocationURL,UriKind.Absolute,out var uri) || !uri.IsFile ||
                        !string.Equals(uri.LocalPath.TrimEnd('\\'),Path.GetDirectoryName(target),StringComparison.OrdinalIgnoreCase)) continue;
                    foreach(dynamic item in entry.Document.SelectedItems()) if(string.Equals((string)item.Path,target,StringComparison.OrdinalIgnoreCase)) selected=true;
                }
            }
            Check(selected,"Explorer opens target directory and selects executable");
        }
        finally
        {
            foreach(dynamic entry in shell.Windows()) if(!before.Contains((int)entry.HWND) && Uri.TryCreate((string)entry.LocationURL,UriKind.Absolute,out var uri) && uri.IsFile &&
                string.Equals(uri.LocalPath.TrimEnd('\\'),Path.GetDirectoryName(target),StringComparison.OrdinalIgnoreCase)) entry.Quit();
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }
}
