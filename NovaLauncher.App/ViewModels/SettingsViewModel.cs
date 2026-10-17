using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using NovaLauncher.App.Models;
using NovaLauncher.App.Persistence;
using NovaLauncher.App.Services;

namespace NovaLauncher.App.ViewModels;

public sealed record StartupPageChoice(StartupPage Value, string Label);

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly IAppSettingsService _service;
    private readonly IToastService _toast;
    private AppSettings _display;
    private bool _isSaving;
    private Task _pending = Task.CompletedTask;
    public SettingsViewModel(IAppSettingsService service, IDataDirectoryService directory, ApplicationDataPaths paths, IToastService toast)
    {
        _service = service; _toast = toast; _display = service.Current;
        DataDirectory = paths.RootDirectory; DatabasePath = paths.DatabasePath;
        OpenDataDirectoryCommand = new AsyncRelayCommand(async () =>
        {
            try { await directory.OpenAsync(); }
            catch (Exception ex) { Trace.TraceError("Open data directory failed: {0}", ex); toast.Show("无法打开数据目录"); }
        });
        AppearanceCommand = new RelayCommand(() => toast.Show("外观设置将在下一阶段提供"));
    }
    public IReadOnlyList<StartupPageChoice> StartupPages { get; } =
        [new(StartupPage.Home, "首页"), new(StartupPage.Recent, "最近使用"), new(StartupPage.Favorites, "收藏")];
    public bool CanEdit => !_isSaving;
    public bool RememberWindowSize { get => _display.RememberWindowSize; set { if (value != RememberWindowSize) Save(s => s with { RememberWindowSize = value }); } }
    public bool RememberWindowPosition { get => _display.RememberWindowPosition; set { if (value != RememberWindowPosition) Save(s => s with { RememberWindowPosition = value }); } }
    public StartupPage StartupPage { get => _display.StartupPage; set { if (value != StartupPage) Save(s => s with { StartupPage = value }); } }
    public string DataDirectory { get; }
    public string DatabasePath { get; }
    public string Version => typeof(SettingsViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(SettingsViewModel).Assembly.GetName().Version?.ToString() ?? "未知";
    public IAsyncRelayCommand OpenDataDirectoryCommand { get; }
    public IRelayCommand AppearanceCommand { get; }
    public void Refresh() { _display = _service.Current; NotifyValues(); }
    public Task FlushAsync() => _pending;
    private void Save(Func<AppSettings, AppSettings> update)
    {
        if (_isSaving) { NotifyValues(); return; }
        _isSaving = true; OnPropertyChanged(nameof(CanEdit));
        _display = update(_display); NotifyValues();
        _pending = SaveAsync(update);
    }
    private async Task SaveAsync(Func<AppSettings, AppSettings> update)
    {
        if (!await _service.UpdateAsync(update)) _toast.Show("设置保存失败");
        Refresh(); _isSaving = false; OnPropertyChanged(nameof(CanEdit));
    }
    private void NotifyValues()
    {
        OnPropertyChanged(nameof(RememberWindowSize)); OnPropertyChanged(nameof(RememberWindowPosition)); OnPropertyChanged(nameof(StartupPage));
    }
}
