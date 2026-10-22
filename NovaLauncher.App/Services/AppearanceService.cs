using System.Diagnostics;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using NovaLauncher.App.Models;
using BackgroundMode = NovaLauncher.App.Models.BackgroundMode;

namespace NovaLauncher.App.Services;

public interface IAppearanceService : System.ComponentModel.INotifyPropertyChanged
{
    AppSettings Preview { get; }
    ImageSource? Image { get; }
    bool HasImage { get; }
    bool NeedsTextSurface { get; }
    bool IsTransparent { get; }
    double OverlayOpacity { get; }
    double BlurRadius { get; }
    Stretch Stretch { get; }
    Task SetModeAsync(BackgroundMode mode);
    Task ImportAsync(string path);
    Task RefreshWallpaperAsync();
    Task RestoreDefaultAsync();
    void PreviewParameters(double opacity, double blur, BackgroundStretch stretch);
    Task FlushAsync();
}

public sealed class AppearanceService(IAppSettingsService settings, BackgroundImageStore images, IDesktopWallpaperSource wallpaper, IToastService toast) : ObservableObject, IAppearanceService
{
    private AppSettings _preview = new();
    private ImageSource? _image;
    private CancellationTokenSource? _delay;
    private Task _pending = Task.CompletedTask;
    private long _revision;
    private readonly SemaphoreSlim _writes = new(1, 1);
    public AppSettings Preview => _preview;
    public ImageSource? Image { get => _image; private set { SetProperty(ref _image, value); OnPropertyChanged(nameof(HasImage)); OnPropertyChanged(nameof(NeedsTextSurface)); } }
    public bool HasImage => Image is not null;
    public bool IsTransparent => _preview.BackgroundMode == BackgroundMode.Transparent;
    public bool NeedsTextSurface => IsTransparent || (HasImage && OverlayOpacity < 0.60);
    public double OverlayOpacity => _preview.BackgroundOverlayOpacity;
    public double BlurRadius => _preview.BackgroundBlurRadius;
    public Stretch Stretch => _preview.BackgroundStretch == BackgroundStretch.Uniform ? Stretch.Uniform : Stretch.UniformToFill;
    private void Publish(AppSettings value)
    {
        _preview = value; OnPropertyChanged(nameof(Preview)); OnPropertyChanged(nameof(OverlayOpacity));
        OnPropertyChanged(nameof(BlurRadius)); OnPropertyChanged(nameof(Stretch)); OnPropertyChanged(nameof(NeedsTextSurface));
        OnPropertyChanged(nameof(IsTransparent));
    }
    public async Task InitializeAsync()
    {
        Publish(settings.Current);
        Image = await ResolveAsync(settings.Current);
        if (IsTransparent && !TransparencySupport.IsSupported())
        {
            await FallBackFromTransparencyAsync();
            toast.Show("当前系统不支持实时透明背景");
        }
    }
    public async Task FallBackFromTransparencyAsync()
    {
        await SetModeAsync(BackgroundMode.Default);
        // Rendering safety cannot depend on whether SQLite is currently writable.
        if (IsTransparent) { Publish(_preview with { BackgroundMode = BackgroundMode.Default }); Image = null; }
    }
    private async Task<ImageSource?> ResolveAsync(AppSettings value)
    {
        if (value.BackgroundMode is BackgroundMode.Default or BackgroundMode.Transparent || (value.BackgroundMode == BackgroundMode.CustomImage && value.BackgroundImagePath is null)) return null;
        try
        {
            string? path = value.BackgroundMode == BackgroundMode.DesktopWallpaper ? await wallpaper.GetPathAsync() : value.BackgroundImagePath;
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) throw new System.IO.FileNotFoundException();
            return await images.LoadAsync(path);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Background loading failed: {0}", ex);
            toast.Show(value.BackgroundMode == BackgroundMode.DesktopWallpaper ? "无法读取桌面壁纸" : "自定义背景文件不存在或无法读取，已恢复默认背景");
            return null; // Session fallback: retain saved preferences so a removable wallpaper can recover on refresh.
        }
    }
    public async Task SetModeAsync(BackgroundMode mode)
    {
        await FlushAsync();
        if (!Enum.IsDefined(mode)) mode = BackgroundMode.Default;
        if (mode == BackgroundMode.Transparent && !TransparencySupport.IsSupported())
        {
            toast.Show("当前系统不支持实时透明背景");
            mode = BackgroundMode.Default;
        }
        var next = settings.Current with { BackgroundMode = mode };
        var image = await ResolveAsync(next);
        if (await settings.UpdateAsync(s => s with { BackgroundMode = mode })) { Publish(settings.Current); Image = image; }
        else toast.Show("设置保存失败");
    }
    public async Task RefreshWallpaperAsync() { await FlushAsync(); Image = await ResolveAsync(_preview); }
    public async Task ImportAsync(string path)
    {
        if (!BackgroundImageStore.Supports(path)) { toast.Show("不支持此图片格式"); return; }
        await FlushAsync();
        string? candidate = null;
        try
        {
            var imported = await images.ImportAsync(path); candidate = imported.Path;
            string? old = settings.Current.BackgroundImagePath;
            if (!await settings.UpdateAsync(s => s with { BackgroundMode = BackgroundMode.CustomImage, BackgroundImagePath = imported.Path }))
            { toast.Show("设置保存失败"); return; }
            Publish(settings.Current); Image = imported.Image; candidate = null;
            await Task.Run(() => images.DeleteOwned(old));
        }
        catch (Exception ex) { Trace.TraceWarning("Background import failed: {0}", ex); toast.Show("无法读取背景图片"); }
        finally { if (candidate is not null) await Task.Run(() => images.DeleteOwned(candidate)); }
    }
    public void PreviewParameters(double opacity, double blur, BackgroundStretch stretch)
    {
        Publish(AppSettingsService.Validate(_preview with { BackgroundOverlayOpacity = opacity, BackgroundBlurRadius = blur, BackgroundStretch = stretch }));
        _revision++;
        _delay?.Cancel(); _delay?.Dispose(); _delay = new();
        _pending = PersistAfterDelayAsync(_preview, _revision, _delay.Token);
    }
    private async Task PersistAfterDelayAsync(AppSettings snapshot, long revision, CancellationToken token)
    {
        try { await Task.Delay(400, token); }
        catch (OperationCanceledException) { return; }
        await _writes.WaitAsync();
        try
        {
            if (revision != _revision) return;
            bool saved = await settings.UpdateAsync(s => s with
            { BackgroundOverlayOpacity = snapshot.BackgroundOverlayOpacity, BackgroundBlurRadius = snapshot.BackgroundBlurRadius, BackgroundStretch = snapshot.BackgroundStretch });
            if (!saved)
            {
                toast.Show("设置保存失败");
                if (revision == _revision) Publish(settings.Current);
            }
        }
        finally { _writes.Release(); }
    }
    public async Task FlushAsync()
    {
        await _pending;
        await _writes.WaitAsync(); _writes.Release();
    }
    public async Task RestoreDefaultAsync()
    {
        await FlushAsync(); var old = settings.Current.BackgroundImagePath;
        if (!await settings.UpdateAsync(s => s with { BackgroundMode = BackgroundMode.Default, BackgroundImagePath = null,
            BackgroundOverlayOpacity = 0.72, BackgroundBlurRadius = 12, BackgroundStretch = BackgroundStretch.UniformToFill }))
        { toast.Show("设置保存失败"); return; }
        Publish(settings.Current); Image = null; await Task.Run(() => images.DeleteOwned(old));
    }
}
