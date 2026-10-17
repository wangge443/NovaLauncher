using System.IO;
using System.Text.Json;
using System.Windows;
using NovaLauncher.App.Models;
using NovaLauncher.App.Persistence;
using NovaLauncher.App.Services;
using NovaLauncher.App.ViewModels;
using Microsoft.EntityFrameworkCore;
namespace NovaLauncher.Verification;
internal static partial class Program
{
    private sealed record AppearanceSnapshot(string Existing,string LegacySettings,int Step,AppSettings? Original=null,byte[]? OriginalImage=null);
    private static string LegacySettings(ApplicationDataPaths paths)
    {
        using var connection=OpenDatabase(paths.DatabasePath,true);
        return Query(connection,"SELECT Id, StartupPage, RememberWindowSize, RememberWindowPosition, WindowWidth, WindowHeight, WindowLeft, WindowTop, BackgroundMode, ThemeMode, UpdatedAt FROM AppSettings ORDER BY Id");
    }
    private static async Task<int> VerifyRealAppearanceAsync()
    {
        var paths=ApplicationDataPaths.Default;
        string snapshot=Path.Combine(Path.GetTempPath(),"Nova-appearance-"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            Directory.CreateDirectory(paths.BackupsDirectory);
            string backup=Path.Combine(paths.BackupsDirectory,$"nova-before-appearance-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}.db");
            using(File.Create(backup)) { }
            using(var source=OpenDatabase(paths.DatabasePath,true)) using(var destination=OpenDatabase(backup)) source.BackupDatabase(destination);
            Console.WriteLine("Backup: "+backup);
            var state=new AppearanceSnapshot(SettingsExistingState(paths),LegacySettings(paths),0);
            for(int step=0;step<3;step++)
            {
                if(step>0) state=JsonSerializer.Deserialize<AppearanceSnapshot>(File.ReadAllText(snapshot))! with {Step=step};
                File.WriteAllText(snapshot,JsonSerializer.Serialize(state));
                await RunCategoryChildAsync("--appearance-real-window",snapshot);
                Check(SettingsExistingState(paths)==state.Existing,"Real old application/category/profile data and hashes preserved after process "+step);
            }
            return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            if(File.Exists(snapshot))
            {
                var state=JsonSerializer.Deserialize<AppearanceSnapshot>(File.ReadAllText(snapshot))!;
                if(state.Original is {} original)
                {
                    if(state.OriginalImage is {} bytes && original.BackgroundImagePath is {} path) await File.WriteAllBytesAsync(path,bytes);
                    await new AppSettingsRepository(new LauncherDbContextFactory(paths)).SaveAsync(original);
                    Check(LegacySettings(paths)==state.LegacySettings,"All original AppSettings fields restored exactly");
                }
                File.Delete(snapshot);
            }
        }
    }
    private static int RunRealAppearanceWindow(string snapshotPath)
    {
        var app=new NovaLauncher.App.App(); app.InitializeComponent();
        app.Startup+=(_,_)=>app.Dispatcher.BeginInvoke(new Action(async ()=>
        {
            try
            {
                await app.ReadyTask; var paths=ApplicationDataPaths.Default;
                var state=JsonSerializer.Deserialize<AppearanceSnapshot>(File.ReadAllText(snapshotPath))!;
                var shell=(MainWindowViewModel)app.MainWindow.DataContext; var vm=shell.Settings!.Appearance!; var appearance=vm.Background;
                Check(SettingsExistingState(paths)==state.Existing,"Production migration preserves every old app category profile field and image hash");
                using(var db=new LauncherDbContextFactory(paths).CreateDbContext()) Check(!(await db.Database.GetPendingMigrationsAsync()).Any() && !db.Database.HasPendingModelChanges(),"Real background migration applied and model matches");
                _screenshots=Path.GetFullPath("docs/screenshots/6.3"); Directory.CreateDirectory(_screenshots);
                if(state.Step==0)
                {
                    Check(LegacySettings(paths)==state.LegacySettings,"Migration preserves all original AppSettings fields");
                    var original=appearance.Preview;
                    state=state with {Original=original,OriginalImage=original.BackgroundImagePath is {} p && File.Exists(p)?await File.ReadAllBytesAsync(p):null};
                    File.WriteAllText(snapshotPath,JsonSerializer.Serialize(state));
                    string fixture=Path.Combine(Path.GetTempPath(),"Nova-background-fixture-"+Guid.NewGuid().ToString("N"));
                    try { await appearance.ImportAsync(BackgroundFixture(fixture,"real.png")); }
                    finally { Directory.Delete(fixture,true); }
                    vm.OverlayPercent=84; vm.BlurRadius=18; vm.Stretch=BackgroundStretch.Uniform; await vm.FlushAsync();
                    Check(appearance.HasImage,"Real custom image committed after source removed");
                }
                else if(state.Step==1)
                {
                    Check(vm.Mode==BackgroundMode.CustomImage && appearance.HasImage && vm.OverlayPercent==84 && vm.BlurRadius==18 && vm.Stretch==BackgroundStretch.Uniform,"Independent restart restores custom image Overlay Blur Fit");
                    app.MainWindow.UpdateLayout(); SaveWindow(app.MainWindow,"real-custom-restarted.png");
                    await appearance.SetModeAsync(BackgroundMode.DesktopWallpaper); vm.OverlayPercent=72; vm.BlurRadius=12; await vm.FlushAsync();
                    Check(appearance.HasImage,"Real DesktopWallpaper loads current static wallpaper");
                }
                else
                {
                    Check(vm.Mode==BackgroundMode.DesktopWallpaper && appearance.HasImage && vm.OverlayPercent==72 && vm.BlurRadius==12,"Independent restart restores desktop wallpaper and parameters");
                    app.MainWindow.UpdateLayout(); SaveWindow(app.MainWindow,"real-desktop-restarted.png");
                    await appearance.RestoreDefaultAsync(); Check(!appearance.HasImage,"Real restore default succeeds");
                }
                app.MainWindow.Close();
            }
            catch(Exception ex) {Console.Error.WriteLine(ex); app.Shutdown(1);}
        })); return app.Run();
    }
}
