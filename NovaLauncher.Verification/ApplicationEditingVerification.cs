using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using NovaLauncher.App.Models;
using NovaLauncher.App.Persistence;
using NovaLauncher.App.Services;
using NovaLauncher.App.ViewModels;
using NovaLauncher.App.Views;

namespace NovaLauncher.Verification;

internal static partial class Program
{
    private sealed class IconPickerStub : IApplicationIconPicker { public string? Path { get; set; } public string? Pick() => Path; }
    private sealed class EditingRepository(IApplicationManagementRepository inner) : IApplicationManagementRepository
    {
        public bool Fail { get; set; }
        public Task RenameAsync(Guid id,string name,CancellationToken token) => Fail ? Task.FromException(new IOException("test failure")) : inner.RenameAsync(id,name,token);
        public Task SetCustomIconAsync(Guid id,string? path,CancellationToken token) => Fail ? Task.FromException(new IOException("test failure")) : inner.SetCustomIconAsync(id,path,token);
        public Task RemoveAsync(Guid id,CancellationToken token) => Fail ? Task.FromException(new IOException("test failure")) : inner.RemoveAsync(id,token);
    }
    private sealed record EditingSnapshot(string Root,Guid Id,string Name,string Path,string Hash);
    private static async Task<int> VerifyEditingRestartAsync(string snapshotFile)
    {
        try
        {
            var snapshot=JsonSerializer.Deserialize<EditingSnapshot>(File.ReadAllText(snapshotFile))!;
            var result=await CreatePersistence(new ApplicationDataPaths(snapshot.Root)).LoadAsync(default);
            var app=result.Applications.Single(x=>x.Id==snapshot.Id);
            Check(app.DisplayName==snapshot.Name,"Independent restart preserves custom name");
            Check(app.CustomIconPath==snapshot.Path && Convert.ToHexString(SHA256.HashData(app.Icon!))==snapshot.Hash,"Independent restart restores custom icon after source deletion");
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }

    private static async Task<int> VerifyRealEditingMigrationAsync()
    {
        try
        {
            var paths=ApplicationDataPaths.Default;
            Directory.CreateDirectory(paths.BackupsDirectory);
            string backup=Path.Combine(paths.BackupsDirectory,$"nova-before-app-edit-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}.db");
            using (File.Create(backup)) { }
            using(var source=OpenDatabase(paths.DatabasePath,true)) using(var destination=OpenDatabase(backup)) source.BackupDatabase(destination);
            Console.WriteLine("Backup: "+backup);
            using var beforeDb=OpenDatabase(paths.DatabasePath,true);
            const string columns="Id, CategoryId, IsFavorite, DisplayName, ExecutablePath, TargetPathKey, SourcePath, SourceType, LaunchCount, LastLaunchTime, DateAdded, SortOrder, IconCachePath";
            string rows=Query(beforeDb,"SELECT "+columns+" FROM Applications ORDER BY Id");
            string other=Query(beforeDb,"SELECT * FROM Categories ORDER BY Id")+Query(beforeDb,"SELECT * FROM UserProfiles ORDER BY Id")+Query(beforeDb,"SELECT * FROM AppSettings ORDER BY Id");
            string files=ManagedFilesState(paths);
            await Initializer(paths).InitializeAsync(default);
            using var after=OpenDatabase(paths.DatabasePath,true);
            Check(Query(after,"SELECT "+columns+" FROM Applications ORDER BY Id")==rows,"Real migration preserves all old Application fields including favorites and statistics");
            Check(Query(after,"SELECT * FROM Categories ORDER BY Id")+Query(after,"SELECT * FROM UserProfiles ORDER BY Id")+Query(after,"SELECT * FROM AppSettings ORDER BY Id")==other,"Real migration preserves Categories Profile AppSettings and background preferences");
            Check(ManagedFilesState(paths)==files,"Real migration preserves icons avatars backgrounds and custom resources");
            using var db=new LauncherDbContextFactory(paths).CreateDbContext();
            Check(!(await db.Database.GetPendingMigrationsAsync()).Any() && !db.Database.HasPendingModelChanges(),"Real migration applied and snapshot matches");
            Console.WriteLine($"PASS: {_checks} real migration assertions"); return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    private static string ManagedFilesState(ApplicationDataPaths paths) => JsonSerializer.Serialize(
        new[]{paths.IconsDirectory,paths.ProfileDirectory,paths.BackgroundDirectory,paths.CustomIconsDirectory}
        .Where(Directory.Exists).SelectMany(x=>Directory.GetFiles(x)).OrderBy(x=>x)
        .Select(x=>x+":"+Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x)))));

    private static async Task RunApplicationEditingAsync(MainWindow window)
    {
        var paths=new ApplicationDataPaths(Path.Combine(_fixtures,"editing"));
        var cache=new ApplicationIconCache(paths,new ApplicationIconService()); var custom=cache.CustomIcons;
        var repo=new EditingRepository(new ApplicationRepository(new LauncherDbContextFactory(paths),paths));
        var management=new ApplicationManagementService(repo,cache,custom); var picker=new IconPickerStub();
        using var content=new ContentViewModel(new ApplicationImportService(new ShortcutResolver(),new ApplicationIconService()),new ApplicationLaunchService(),CreatePersistence(paths),new ToastService(),CategoryStore(paths),new LocationProbe(),management,picker);
        await content.InitializeAsync();
        var settings=SettingsService(paths); await settings.LoadAsync();
        string exe=CopyProbe("真实EXE测试"); string target=CopyProbe("真实Shortcut目标"); string link=MakeConfiguredShortcut(target,_fixtures);
        string exeHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(exe))); string linkHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(link)));
        await content.ImportAsync([exe,link]);
        var first=content.Applications.Single(x=>x.TargetPath==exe); var second=content.Applications.Single(x=>x.TargetPath==target);
        // LaunchProbe has no embedded icon; seed a valid automatic cache to exercise cache fallback.
        byte[] cachedIcon=File.ReadAllBytes(MakeAvatar(_fixtures,"automatic-cache.png"));
        await cache.WriteAsync(first.Application.Id,cachedIcon,default);
        await first.ToggleFavoriteCommand.ExecuteAsync(null); await first.LaunchCommand.ExecuteAsync(null);
        var game=content.CategoryManager.Categories.First(); await first.ChangeCategoryCommand.ExecuteAsync(new(game.Id,game.Name));
        var shell=new MainWindowViewModel(content,settings:new SettingsViewModel(settings,new SettingsDirectoryStub(),paths,content.Toast));
        window.DataContext=shell; window.Topmost=true; window.Activate(); window.UpdateLayout();
        using(var db=new LauncherDbContextFactory(paths).CreateDbContext()) Check(!db.Database.HasPendingModelChanges() && !(await db.Database.GetPendingMigrationsAsync()).Any(),"CustomIconPath migration and snapshot match");
        var card=AllCardViews(window).Single(x=>x.DataContext==first);
        await OpenCardMenuAsync(card); Check(((MenuItem)card.FindName("RestoreIconMenuItem")).Visibility==Visibility.Collapsed,"Restore icon hidden without custom icon");
        Check((string)((MenuItem)card.ContextMenu.Items[^1]).Header=="从启动器移除","Removal action is last menu item");
        CaptureCategoryWindow(window,"context-menu.png");
        await ActivateMenuAsync((MenuItem)card.FindName("RenameMenuItem")); var rename=(Popup)card.FindName("RenamePopup"); await WaitForAsync(()=>rename.IsOpen);
        var editor=(TextBox)card.FindName("RenameEditor"); editor.Text="   ";
        Check(!first.RenameCommand.CanExecute(null),"Blank rename prohibited"); editor.Text=new string('长',49); Check(!first.RenameCommand.CanExecute(null),"Overlong rename prohibited");
        editor.Text="取消更名"; ProfileKey(0x1B); await WaitForAsync(()=>!rename.IsOpen); Check(first.DisplayName!="取消更名","Escape cancels rename");
        await OpenCardMenuAsync(card); await ActivateMenuAsync((MenuItem)card.FindName("RenameMenuItem")); await WaitForAsync(()=>rename.IsOpen);
        editor.Text="  我的测试应用  "; await Task.Delay(250); CaptureCategoryWindow(window,"rename.png"); editor.Focus(); ProfileKey(0x0D);
        await WaitForAsync(()=>first.DisplayName=="我的测试应用" && !rename.IsOpen);
        Check(first.LaunchCount==1,"Rename Enter trims and does not launch");
        Check(AllCardViews(window).Single(x=>x.DataContext==first).DataContext==content.Home.FavoriteApplications.Single(),"Home projections share renamed ViewModel");
        foreach(var nav in new[]{shell.Sidebar.FooterItems[0],shell.Sidebar.FooterItems[1],game})
        {
            shell.Sidebar.SelectedItem=nav; window.UpdateLayout(); Check(Descendants<ApplicationCardView>(window).Any(x=>x.DataContext==first) && first.DisplayName=="我的测试应用","Rename visible in "+nav.Name);
        }
        repo.Fail=true; first.RenameDraft="保存失败的名称"; await first.RenameCommand.ExecuteAsync(null);
        Check(first.DisplayName=="我的测试应用" && content.Toast.Message=="名称保存失败","Rename failure preserves old name with toast"); repo.Fail=false;
        string png=MakeAvatar(_fixtures,"icon-a.png");
        picker.Path=await NativeProfilePickerAsync(png,"更换应用图标",new ApplicationIconPicker().Pick); await first.ChangeIconCommand.ExecuteAsync(null);
        Check(first.HasCustomIcon && first.Application.CustomIconPath!.StartsWith(paths.CustomIconsDirectory),"Native PNG picker saves owned CustomIcons PNG");
        string old=first.Application.CustomIconPath!; File.Delete(png); Check(File.Exists(old),"Deleting selected source leaves owned icon intact");
        string ico=Path.Combine(_fixtures,"icon.ico"); byte[] payload=File.ReadAllBytes(old);
        using(var stream=File.Create(ico)) using(var writer=new BinaryWriter(stream))
        { writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)1);writer.Write((byte)96);writer.Write((byte)64);writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(payload.Length);writer.Write(22);writer.Write(payload); }
        picker.Path=await NativeProfilePickerAsync(ico,"更换应用图标",new ApplicationIconPicker().Pick); await first.ChangeIconCommand.ExecuteAsync(null);
        Check(first.HasCustomIcon && !File.Exists(old),"Native ICO selection replaces icon and cleans prior file without lock");
        foreach(string extension in new[]{"jpg","jpeg","bmp"})
        {
            old=first.Application.CustomIconPath!;
            picker.Path=MakeAvatar(_fixtures,"icon."+extension,true,extension=="bmp"?new System.Windows.Media.Imaging.BmpBitmapEncoder():new System.Windows.Media.Imaging.JpegBitmapEncoder());
            await first.ChangeIconCommand.ExecuteAsync(null);
            Check(!File.Exists(old) && File.Exists(first.Application.CustomIconPath),"Repeated replacement supports "+extension+" without lock");
        }
        old=first.Application.CustomIconPath!; picker.Path=await NativeProfilePickerAsync(null,"更换应用图标",new ApplicationIconPicker().Pick); await first.ChangeIconCommand.ExecuteAsync(null);
        Check(first.Application.CustomIconPath==old,"Picker cancel preserves icon");
        picker.Path=Path.Combine(_fixtures,"invalid.txt"); await first.ChangeIconCommand.ExecuteAsync(null);
        Check(first.Application.CustomIconPath==old && content.Toast.Message=="不支持此图片格式","Unsupported icon format toast");
        picker.Path=Path.Combine(_fixtures,"damaged.png"); File.WriteAllText(picker.Path,"not image"); await first.ChangeIconCommand.ExecuteAsync(null);
        Check(first.Application.CustomIconPath==old && content.Toast.Message=="无法读取该图片","Damaged icon keeps old icon and reports toast");
        repo.Fail=true; picker.Path=ico; int files=Directory.GetFiles(paths.CustomIconsDirectory).Length; await first.ChangeIconCommand.ExecuteAsync(null);
        Check(first.Application.CustomIconPath==old && File.Exists(old) && Directory.GetFiles(paths.CustomIconsDirectory).Length==files && content.Toast.Message=="图标保存失败","Icon database failure keeps old icon and cleans candidate");
        await first.RestoreIconCommand.ExecuteAsync(null); Check(first.HasCustomIcon && File.Exists(old),"Restore failure preserves old custom icon"); repo.Fail=false;
        await WaitForAsync(()=>!content.Toast.IsOpen);
        shell.Sidebar.SelectedItem=shell.Sidebar.Items[0]; window.UpdateLayout();
        Check(Descendants<ApplicationCardView>(window).Where(x=>x.DataContext==first).Count()==3,"Custom icon shared by three Home sections");
        Check(Descendants<ApplicationCardView>(window).Where(x=>x.DataContext==first).SelectMany(x=>Descendants<Image>(x)).All(x=>ReferenceEquals(x.Source,first.Icon)),"All Home card images update immediately");
        await Task.Delay(350); SaveWindow(window,"custom-icon.png");
        foreach(var nav in new[]{shell.Sidebar.FooterItems[0],shell.Sidebar.FooterItems[1],game})
        { shell.Sidebar.SelectedItem=nav;window.UpdateLayout();Check(Descendants<ApplicationCardView>(window).Where(x=>x.DataContext==first).SelectMany(x=>Descendants<Image>(x)).All(x=>ReferenceEquals(x.Source,first.Icon)),"Custom icon synchronized in "+nav.Name); }
        foreach(string source in new[]{ico,Path.Combine(_fixtures,"icon.jpg"),Path.Combine(_fixtures,"icon.jpeg"),Path.Combine(_fixtures,"icon.bmp")}) File.Delete(source);
        string snapshot=Path.Combine(_fixtures,"editing-restart.json");
        File.WriteAllText(snapshot,JsonSerializer.Serialize(new EditingSnapshot(paths.RootDirectory,first.Application.Id,first.DisplayName,old,Convert.ToHexString(SHA256.HashData(first.Application.Icon!)))));
        await RunCategoryChildAsync("--app-edit-restart",snapshot);
        var loaded=(await CreatePersistence(paths).LoadAsync(default)).Applications.Single(x=>x.Id==first.Application.Id);
        Check(loaded.DisplayName==first.DisplayName && loaded.CustomIconPath==old,"Load does not overwrite name with EXE metadata");
        byte[] owned=File.ReadAllBytes(old); File.Delete(old);
        loaded=(await CreatePersistence(paths).LoadAsync(default)).Applications.Single(x=>x.Id==first.Application.Id);
        Check(loaded.Icon is not null && loaded.CustomIconPath==old,"Missing custom icon safely falls back to extracted cache");
        File.WriteAllText(old,"damaged"); loaded=(await CreatePersistence(paths).LoadAsync(default)).Applications.Single(x=>x.Id==first.Application.Id);
        Check(loaded.Icon is not null,"Corrupt custom icon safely falls back");
        File.Delete(paths.IconPath(first.Application.Id));
        loaded=(await CreatePersistence(paths).LoadAsync(default)).Applications.Single(x=>x.Id==first.Application.Id);
        using(var fallback=new ApplicationCardViewModel(loaded,new ApplicationLaunchService(),CreatePersistence(paths)))
            Check(loaded.Icon is null && fallback.Icon is System.Windows.Media.DrawingImage,"No embedded EXE icon falls back to Nova default");
        await cache.WriteAsync(first.Application.Id,cachedIcon,default);
        File.WriteAllBytes(old,owned);
        await first.RestoreIconCommand.ExecuteAsync(null); Check(!first.HasCustomIcon && !File.Exists(old) && File.Exists(paths.IconPath(first.Application.Id)),"Restore clears DB pointer and owned file, preserves default cache");
        picker.Path=MakeAvatar(_fixtures,"last.png",true); await first.ChangeIconCommand.ExecuteAsync(null); old=first.Application.CustomIconPath!;
        shell.Sidebar.SelectedItem=shell.Sidebar.Items[0];window.UpdateLayout(); card=AllCardViews(window).Single(x=>x.DataContext==first);
        await OpenCardMenuAsync(card); Check(((MenuItem)card.FindName("RestoreIconMenuItem")).Visibility==Visibility.Visible,"Restore action visible with custom icon");
        await ActivateMenuAsync((MenuItem)card.FindName("RemoveMenuItem"));var remove=(Popup)card.FindName("RemovePopup");await WaitForAsync(()=>remove.IsOpen);
        Check(Descendants<TextBlock>(remove.Child).Any(x=>x.Text=="不会删除电脑中的程序或文件。"),"Internal confirmation explains file safety");
        Check(((Button)card.FindName("CancelRemoveButton")).IsKeyboardFocused,"Removal initially focuses cancel"); CaptureCategoryWindow(window,"remove-confirm.png");
        ProfileKey(0x0D);await WaitForAsync(()=>!remove.IsOpen);Check(content.Applications.Contains(first),"Cancel confirmation leaves record and UI");
        repo.Fail=true;await first.RemoveCommand.ExecuteAsync(null);Check(content.Applications.Contains(first) && File.Exists(old) && content.Toast.Message=="无法移除该应用","Database deletion failure retains UI and files");repo.Fail=false;
        await WaitForAsync(()=>!content.Toast.IsOpen); await OpenCardMenuAsync(card);await ActivateMenuAsync((MenuItem)card.FindName("RemoveMenuItem"));await WaitForAsync(()=>remove.IsOpen);
        ((Button)card.FindName("ConfirmRemoveButton")).Focus();ProfileKey(0x0D);await WaitForAsync(()=>!content.Applications.Contains(first));
        Check(!content.Home.AllApplications.Contains(first)&&!content.Home.FavoriteApplications.Contains(first)&&!content.Home.RecentApplications.Contains(first),"Confirmed removal updates all Home projections immediately");
        using(var db=new LauncherDbContextFactory(paths).CreateDbContext()) Check(!await db.Applications.AnyAsync(x=>x.Id==first.Application.Id) && await db.Categories.AnyAsync(x=>x.Id==game.Id),"SQLite deletes only application row and preserves category");
        Check(File.Exists(exe)&&Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(exe)))==exeHash,"Real test EXE still exists with identical bytes");
        Check(!File.Exists(old)&&!File.Exists(paths.IconPath(first.Application.Id)),"Only Nova custom icon and automatic cache cleaned");
        foreach(var nav in new[]{shell.Sidebar.FooterItems[0],shell.Sidebar.FooterItems[1],game}){shell.Sidebar.SelectedItem=nav;Check(!content.FilteredApplications.Cast<object>().Contains(first),"Removed application absent from "+nav.Name);}
        await second.RemoveCommand.ExecuteAsync(null);
        Check(File.Exists(link)&&File.Exists(target)&&Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(link)))==linkHash,"Real LNK and target remain after removal");
        Check(content.Applications.Count==0,"Removal preserves collection consistency");
        await content.ImportAsync([exe,link]);Check(content.Applications.Count==2&&content.Applications.All(x=>x.Application.Id!=first.Application.Id&&x.Application.Id!=second.Application.Id),"Reimport identical EXE and LNK creates fresh records");
        Check(content.Applications.All(x=>!x.IsFavorite&&x.LaunchCount==0&&x.CategoryId is null&&!x.HasCustomIcon),"Reimport starts with new default metadata");
        var preserved=content.Applications.Last(); string external=MakeAvatar(_fixtures,"do-not-delete.png");custom.DeleteOwned(preserved.Application.Id,external);
        Check(File.Exists(external),"Cleanup rejects paths outside CustomIcons");
        Check(first.LaunchCount==1 && second.LaunchCount==0,"All edit and remove operations never launch targets");
        picker.Path=MakeAvatar(_fixtures,"cleanup.png"); await preserved.ChangeIconCommand.ExecuteAsync(null);
        string lockedPath=preserved.Application.CustomIconPath!;
        using(var locked=new FileStream(lockedPath,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            await preserved.RestoreIconCommand.ExecuteAsync(null);
            Check(!preserved.HasCustomIcon && File.Exists(lockedPath),"Cleanup failure does not undo successful restore");
        }
        custom.DeleteOwned(preserved.Application.Id,lockedPath);
        var launcher=new ControlledLauncher();
        using var pendingCard=new ApplicationCardViewModel(preserved.Application,launcher,CreatePersistence(paths),management:management);
        bool removed=false; pendingCard.Removed+=_=>removed=true;
        Task launch=pendingCard.LaunchCommand.ExecuteAsync(null);
        Task removal=pendingCard.RemoveCommand.ExecuteAsync(null);
        Check(!removal.IsCompleted && !pendingCard.LaunchCommand.CanExecute(null) && !pendingCard.ToggleFavoriteCommand.CanExecute(null),"Removal waits for in-flight launch and blocks new writes");
        launcher.Complete(new(true)); await Task.WhenAll(launch,removal);
        using(var db=new LauncherDbContextFactory(paths).CreateDbContext())
            Check(removed && !await db.Applications.AnyAsync(x=>x.Id==preserved.Application.Id),"In-flight statistics finish before deletion without resurrecting record");
    }
}
