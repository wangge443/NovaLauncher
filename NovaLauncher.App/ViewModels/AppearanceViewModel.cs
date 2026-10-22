using CommunityToolkit.Mvvm.Input;
using NovaLauncher.App.Models;
using NovaLauncher.App.Services;
namespace NovaLauncher.App.ViewModels;
public sealed record BackgroundModeChoice(BackgroundMode Value, string Label);
public sealed record BackgroundStretchChoice(BackgroundStretch Value, string Label);
public sealed class AppearanceViewModel : ViewModelBase
{
    private readonly IAppearanceService _service;
    private readonly IToastService _toast;
    private bool _busy;
    public AppearanceViewModel(IAppearanceService service, IBackgroundFilePicker picker, IToastService toast)
    {
        _service = service;
        _toast = toast;
        service.PropertyChanged += (_, _) => { OnPropertyChanged(nameof(Mode)); OnPropertyChanged(nameof(IsCustom)); OnPropertyChanged(nameof(IsDesktop)); OnPropertyChanged(nameof(IsImageMode)); OnPropertyChanged(nameof(IsTransparent)); OnPropertyChanged(nameof(ShowTransparencyStatus)); OnPropertyChanged(nameof(TransparencyStatus)); OnPropertyChanged(nameof(GlassOverlayPercent)); OnPropertyChanged(nameof(OverlayPercent)); OnPropertyChanged(nameof(BlurRadius)); OnPropertyChanged(nameof(Stretch)); OnPropertyChanged(nameof(ModeLabel)); };
        ChangeModeCommand = new AsyncRelayCommand<BackgroundModeChoice>(choice => RunAsync(() => service.SetModeAsync(choice?.Value ?? BackgroundMode.Default)));
        ChooseImageCommand = new AsyncRelayCommand(() => RunAsync(async () => { string? path = picker.Pick(); if (path is not null) await service.ImportAsync(path); }));
        RefreshCommand = new AsyncRelayCommand(() => RunAsync(service.RefreshWallpaperAsync));
        RestoreCommand = new AsyncRelayCommand(() => RunAsync(service.RestoreDefaultAsync));
    }
    public IAppearanceService Background => _service;
    public IReadOnlyList<BackgroundModeChoice> Modes { get; } = [new(BackgroundMode.Default,"默认"),new(BackgroundMode.DesktopWallpaper,"桌面壁纸"),new(BackgroundMode.CustomImage,"自定义图片"),new(BackgroundMode.Transparent,"玻璃模式（实验性）")];
    public IReadOnlyList<BackgroundStretchChoice> Stretches { get; } = [new(BackgroundStretch.UniformToFill,"填充"),new(BackgroundStretch.Uniform,"适应")];
    public BackgroundMode Mode { get => _service.Preview.BackgroundMode; set { if (value != Mode && CanEdit) ChangeModeCommand.Execute(Modes.Single(x => x.Value == value)); } }
    public string ModeLabel => Modes.Single(x => x.Value == Mode).Label;
    public bool IsCustom => Mode == BackgroundMode.CustomImage;
    public bool IsDesktop => Mode == BackgroundMode.DesktopWallpaper;
    public bool IsImageMode => Mode is BackgroundMode.DesktopWallpaper or BackgroundMode.CustomImage;
    public bool IsTransparent => Mode == BackgroundMode.Transparent;
    public bool ShowTransparencyStatus => IsTransparent || !TransparencySupport.IsSupported();
    public string TransparencyStatus => TransparencySupport.IsSupported() ? "实时显示窗口后方内容；实验性模式，不提供背景模糊" : "当前系统不支持实时透明背景";
    // Same physical tint concept: reuse the existing field, no duplicate persisted column.
    public double GlassOverlayPercent { get => OverlayPercent; set => OverlayPercent = value; }
    public bool CanEdit => !_busy;
    public double OverlayPercent { get => _service.OverlayOpacity * 100; set { if (CanEdit) _service.PreviewParameters(value / 100, BlurRadius, Stretch); } }
    public double BlurRadius { get => _service.BlurRadius; set { if (CanEdit) _service.PreviewParameters(OverlayPercent / 100, value, Stretch); } }
    public BackgroundStretch Stretch { get => _service.Preview.BackgroundStretch; set { if (CanEdit && Enum.IsDefined(value)) _service.PreviewParameters(OverlayPercent / 100, BlurRadius, value); } }
    public IAsyncRelayCommand<BackgroundModeChoice> ChangeModeCommand { get; }
    public IAsyncRelayCommand ChooseImageCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand RestoreCommand { get; }
    private async Task RunAsync(Func<Task> work)
    {
        if (_busy) return; _busy = true; OnPropertyChanged(nameof(CanEdit));
        try { await work(); }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning("Background command failed: {0}", ex);
            _toast.Show("无法读取背景图片");
        }
        finally { _busy = false; OnPropertyChanged(nameof(CanEdit)); }
    }
    public async Task FlushAsync()
    {
        await Task.WhenAll(ChangeModeCommand.ExecutionTask ?? Task.CompletedTask, ChooseImageCommand.ExecutionTask ?? Task.CompletedTask,
            RefreshCommand.ExecutionTask ?? Task.CompletedTask, RestoreCommand.ExecutionTask ?? Task.CompletedTask);
        await _service.FlushAsync();
    }
}
