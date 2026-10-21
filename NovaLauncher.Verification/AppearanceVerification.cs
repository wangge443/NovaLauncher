using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using NovaLauncher.App.Models;
using NovaLauncher.App.Persistence;
using NovaLauncher.App.Services;
using NovaLauncher.App.ViewModels;
using NovaLauncher.App.Views;
using BackgroundMode = NovaLauncher.App.Models.BackgroundMode;
namespace NovaLauncher.Verification;
internal static partial class Program
{
    private sealed class BackgroundPicker : IBackgroundFilePicker { public string? Path { get; set; } public string? Pick() => Path; }
    private sealed class WallpaperStub(string path) : IDesktopWallpaperSource { public bool Fail { get; set; } public Task<string> GetPathAsync() => Fail ? Task.FromException<string>(new IOException("wallpaper failed")) : Task.FromResult(path); }
    private sealed class CountSettings(IAppSettingsRepository inner) : IAppSettingsRepository
    {
        public int Writes { get; private set; }
        public bool Fail { get; set; }
        public Task<AppSettings> GetOrCreateAsync(CancellationToken token=default) => inner.GetOrCreateAsync(token);
        public Task<AppSettings> SaveAsync(AppSettings original,AppSettings value,CancellationToken token=default) { Writes++; return Fail ? Task.FromException<AppSettings>(new IOException("write failed")) : inner.SaveAsync(original,value,token); }
    }
    private static string BackgroundFixture(string folder, string name, BitmapEncoder? encoder = null)
    {
        Directory.CreateDirectory(folder); const int width=1600,height=1000;
        var pixels=new byte[width*height*4];
        for(int y=0;y<height;y++) for(int x=0;x<width;x++)
        {
            int i=(y*width+x)*4; double wave=(Math.Sin(x/160d+y/130d)+1)/2;
            pixels[i]=(byte)(60+180*wave); pixels[i+1]=(byte)(40+150*y/height); pixels[i+2]=(byte)(40+180*x/width); pixels[i+3]=255;
        }
        var source=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);
        encoder??=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
        var path=Path.Combine(folder,name); using var file=File.Create(path); encoder.Save(file); return path;
    }
    private static async Task RunAppearanceAsync(MainWindow window)
    {
        var paths=new ApplicationDataPaths(Path.Combine(_fixtures,"appearance"));
        using var content=RecentContent(paths); await content.InitializeAsync();
        await content.ImportAsync([Path.Combine(Environment.SystemDirectory,"notepad.exe"), CopyProbe("background-card")]);
        var profile=new UserProfileViewModel(ProfileService(paths),new ProfilePicker(),content.Toast); await profile.InitializeAsync();
        var repository=new CountSettings(new AppSettingsRepository(new LauncherDbContextFactory(paths)));
        var settings=new AppSettingsService(repository); await settings.LoadAsync();
        string before=SettingsExistingState(paths);
        string png=BackgroundFixture(_fixtures,"background.png"); string jpg=BackgroundFixture(_fixtures,"background.jpg",new JpegBitmapEncoder());
        var wallpaper=new WallpaperStub(png); var store=new BackgroundImageStore(paths);
        var appearance=new AppearanceService(settings,store,wallpaper,content.Toast); await appearance.InitializeAsync();
        var picker=new BackgroundPicker(); var vm=new AppearanceViewModel(appearance,picker,content.Toast);
        var settingsVm=new SettingsViewModel(settings,new SettingsDirectoryStub(),paths,content.Toast,vm);
        var shell=new MainWindowViewModel(content,profile,settingsVm); window.DataContext=shell; window.Topmost=true; window.Activate();
        await Task.Delay(150); window.UpdateLayout();
        var background=Descendants<ShellBackgroundView>(window).Single();
        Check(!appearance.HasImage && window.Opacity==1 && !background.IsBackdropActive,"Default uses solid fallback without native backdrop or window opacity");
        SaveWindow(window,"default.png");
        using(var db=new LauncherDbContextFactory(paths).CreateDbContext()) Check(!db.Database.HasPendingModelChanges() && !(await db.Database.GetPendingMigrationsAsync()).Any(),"Background migration and snapshot match");
        Check(settings.Current.BackgroundOverlayOpacity==0.72 && settings.Current.BackgroundBlurRadius==12 && settings.Current.BackgroundStretch==BackgroundStretch.UniformToFill,"Background defaults seeded");
        picker.Path=await NativeProfilePickerAsync(png, "选择背景图片", new BackgroundFilePicker().Pick); Check(picker.Path==png,"Native PNG picker returns selected path"); await vm.ChooseImageCommand.ExecuteAsync(null); Console.WriteLine("Import status: "+content.Toast.Message);
        Check(appearance.HasImage && settings.Current.BackgroundMode==BackgroundMode.CustomImage,"Custom PNG imported");
        string owned=settings.Current.BackgroundImagePath!;
        Check(owned.StartsWith(paths.BackgroundDirectory) && owned.EndsWith(".png"),"Custom source copied to owned versioned PNG");
        File.Delete(png); Check(appearance.HasImage && File.Exists(owned),"Deleting source does not affect owned background");
        picker.Path=await NativeProfilePickerAsync(jpg, "选择背景图片", new BackgroundFilePicker().Pick); await vm.ChooseImageCommand.ExecuteAsync(null);
        Check(appearance.HasImage && !File.Exists(owned),"JPG conversion replaces background and cleans old PNG");
        for(int i=0;i<4;i++) await appearance.ImportAsync(jpg);
        using(var file=new FileStream(settings.Current.BackgroundImagePath!,FileMode.Open,FileAccess.ReadWrite,FileShare.None)) Check(file.Length>0,"Repeated replacement leaves no image file lock");
        Check(Directory.GetFiles(paths.BackgroundDirectory).Length==1,"Only committed image remains");
        picker.Path=await NativeProfilePickerAsync(null, "选择背景图片", new BackgroundFilePicker().Pick); owned=settings.Current.BackgroundImagePath!; await vm.ChooseImageCommand.ExecuteAsync(null);
        Check(settings.Current.BackgroundImagePath==owned,"Cancelled picker preserves background");
        await appearance.ImportAsync("bad.gif"); Check(content.Toast.Message=="不支持此图片格式" && settings.Current.BackgroundImagePath==owned,"Unsupported format preserves image with toast");
        var damaged=Path.Combine(_fixtures,"bad.png"); File.WriteAllText(damaged,"invalid"); await appearance.ImportAsync(damaged);
        Check(content.Toast.Message=="无法读取背景图片" && settings.Current.BackgroundImagePath==owned,"Damaged image preserves old background");
        repository.Fail=true; await appearance.ImportAsync(jpg);
        Check(settings.Current.BackgroundImagePath==owned && File.Exists(owned) && Directory.GetFiles(paths.BackgroundDirectory).Length==1,"Failed database commit preserves old file and removes candidate"); repository.Fail=false;
        await WaitForAsync(()=>!content.Toast.IsOpen); await Task.Delay(200); window.UpdateLayout(); SaveWindow(window,"custom-image.png");
        var image=(Image)background.FindName("WallpaperImage"); var effect=(BlurEffect)image.Effect;
        Check(image.Source is not null && effect.Radius==12 && window.Effect is null && Descendants<ApplicationCardView>(window).All(x=>x.Effect is null),"Blur only applies to background image");
        int writes=repository.Writes;
        for(int i=20;i<=85;i++) vm.OverlayPercent=i;
        Check(appearance.OverlayOpacity==0.85 && repository.Writes==writes,"Slider previews without per-tick SQLite writes");
        vm.BlurRadius=24; vm.Stretch=BackgroundStretch.Uniform; await vm.FlushAsync();
        Check(repository.Writes==writes+1,"400 ms debounce coalesces slider writes");
        window.UpdateLayout(); Check(ReferenceEquals(image.Effect,effect) && effect.Radius==24 && image.Stretch==Stretch.Uniform,"Blur effect reused and Fit applied");
        SaveWindow(window,"overlay85-blur24-fit.png");
        vm.OverlayPercent=20; vm.BlurRadius=0; vm.Stretch=BackgroundStretch.UniformToFill; await vm.FlushAsync(); window.UpdateLayout();
        SaveWindow(window,"overlay20-blur0-fill.png");
        Check(image.Stretch==Stretch.UniformToFill && effect.Radius==0,"Fill and zero blur apply live");
        repository.Fail=true; vm.OverlayPercent=90; await vm.FlushAsync();
        Check(vm.OverlayPercent==20 && content.Toast.Message=="设置保存失败","Failed slider save rolls preview back"); repository.Fail=false;
        vm.OverlayPercent=72; vm.BlurRadius=12; await vm.FlushAsync();
        var loaded=SettingsService(paths); await loaded.LoadAsync(); var restored=new AppearanceService(loaded,store,wallpaper,content.Toast); await restored.InitializeAsync();
        Check(restored.HasImage && restored.BlurRadius==12 && restored.OverlayOpacity==0.72,"Fresh service restores custom image and parameters");
        var timer=Stopwatch.StartNew();
        for(int i=0;i<30;i++) { window.Width=900+i*5; window.UpdateLayout(); await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render); }
        Console.WriteLine($"Blur12 resize: {timer.ElapsedMilliseconds} ms / 30 layouts");
        Check(window.Opacity==1 && Descendants<ApplicationCardView>(window).All(x=>x.ActualWidth==136),"Background resize preserves UI geometry and opacity");
        // Render a scrollable Home with the real background effect active; no launches or database imports.
        var template=content.Applications[0].Application;
        for(int i=0;i<80;i++) content.Applications.Add(new ApplicationCardViewModel(template with {Id=Guid.NewGuid(),DisplayName="背景性能 "+i},new ApplicationLaunchService(),new MemoryPersistence(),content.CategoryManager,content.Toast));
        window.UpdateLayout(); var homeScroll=Descendants<ScrollViewer>(Descendants<AllApplicationsView>(window).Single()).Single();
        timer.Restart();
        for(int i=0;i<30;i++) {homeScroll.ScrollToVerticalOffset(i*40); window.UpdateLayout(); await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);}
        Console.WriteLine($"Blur12 Home scroll: {timer.ElapsedMilliseconds} ms / 30 render yields, 82 cards");
        Check(homeScroll.VerticalOffset>0,"Home scroll works with custom image and Blur");
        homeScroll.ScrollToTop(); window.UpdateLayout();
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ContextIdle);
        await WaitForAsync(()=>homeScroll.VerticalOffset==0); window.UpdateLayout();
        var hovered=AllCardViews(window)[0]; var hit=hovered.PointToScreen(new Point(65,65)); Native.SetCursorPos((int)hit.X,(int)hit.Y);
        await WaitForAsync(()=>hovered.IsMouseOver); await Task.Delay(200);
        Check(((FrameworkElement)hovered.FindName("DetailsContent")).Opacity>0.99,"Card hover remains responsive with Blur");
        SaveWindow(window,"custom-hover.png");
        profile.ToggleProfileFlyoutCommand.Execute(null); await Task.Delay(150);
        Check(profile.IsProfileFlyoutOpen,"Profile Flyout opens above background"); profile.CloseProfileFlyoutCommand.Execute(null);
        while(content.Applications.Count>2) {var last=content.Applications[^1]; content.Applications.RemoveAt(content.Applications.Count-1); last.Dispose();}
        foreach(var item in shell.Sidebar.Items.Concat(shell.Sidebar.FooterItems)) { shell.Sidebar.SelectedItem=item; window.UpdateLayout(); Check(appearance.HasImage,"Background retained on "+item.Name); }
        await WaitForAsync(()=>!content.Toast.IsOpen); await Task.Delay(200); shell.Sidebar.OpenSettings(); await Task.Delay(100); var settingsView=Descendants<SettingsView>(window).Single();
        ((Expander)settingsView.FindName("BackgroundExpander")).IsExpanded=true; window.UpdateLayout();
        var slider=Descendants<Slider>(settingsView).First(); slider.BringIntoView(); window.Activate();
        LaunchNative.SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(window).Handle);
        slider.Focus(); await WaitForAsync(() => slider.IsKeyboardFocused && window.IsActive);
        ProfileKey(0x27); await WaitForAsync(() => vm.OverlayPercent == 73);
        Check(vm.OverlayPercent==73,"Slider native Right key updates preview"); await vm.FlushAsync(); vm.OverlayPercent=72; await vm.FlushAsync();
        SaveWindow(window,"appearance-settings.png");
        Check(Descendants<ScrollViewer>(settingsView).Count(x=>x.TemplatedParent is not TextBox)==1,"Appearance retains one Settings scroll viewer");
        window.Width=800; window.Height=560; ((ColumnDefinition)window.FindName("SidebarColumn")).Width=new GridLength(280); window.UpdateLayout(); SaveWindow(window,"appearance-small.png");
        await appearance.SetModeAsync(BackgroundMode.DesktopWallpaper);
        Check(!appearance.HasImage && content.Toast.Message=="无法读取桌面壁纸","Missing wallpaper falls back without black window");
        wallpaper=new WallpaperStub(jpg); var desktop=new AppearanceService(settings,store,wallpaper,content.Toast); await desktop.InitializeAsync();
        Check(desktop.HasImage,"DesktopWallpaper restores through wallpaper provider");
        wallpaper.Fail=true; await desktop.RefreshWallpaperAsync(); Check(!desktop.HasImage,"Wallpaper refresh failure falls back");
        await appearance.SetModeAsync(BackgroundMode.CustomImage); File.Delete(settings.Current.BackgroundImagePath!);
        await appearance.InitializeAsync(); Check(!appearance.HasImage,"Missing owned image falls back safely");
        await appearance.RestoreDefaultAsync(); Check(settings.Current is {BackgroundMode:BackgroundMode.Default,BackgroundImagePath:null,BackgroundBlurRadius:12,BackgroundOverlayOpacity:0.72} && !appearance.HasImage,"Restore Default clears path and resets parameters");
        Check(SettingsExistingState(paths)==before,"All prior app category favorite launch profile data preserved");
        var realWallpaper=new DesktopWallpaperSource(); string desktopPath=await realWallpaper.GetPathAsync();
        var live=new AppearanceService(settings,store,realWallpaper,content.Toast); await live.SetModeAsync(BackgroundMode.DesktopWallpaper);
        Check(live.HasImage && File.Exists(desktopPath),"Official Windows wallpaper API loads actual local wallpaper");
        var liveVm=new AppearanceViewModel(live,picker,content.Toast); window.DataContext=new MainWindowViewModel(content,profile,new SettingsViewModel(settings,new SettingsDirectoryStub(),paths,content.Toast,liveVm));
        shell.Sidebar.SelectedItem=shell.Sidebar.Items[0]; window.Width=1120; window.Height=720; await WaitForAsync(()=>!content.Toast.IsOpen); await Task.Delay(200); window.UpdateLayout(); SaveWindow(window,"desktop-wallpaper.png");
    }
}
