using NovaLauncher.App.Services;

namespace NovaLauncher.App.ViewModels;

public sealed class BackgroundStartupViewModel(IAppSettingsService settings, IStartupRegistrationService startup, IToastService toast) : ViewModelBase
{
    private bool _startWithWindows;
    private bool _busy;
    private string? _status;
    private Task _pending = Task.CompletedTask;
    public bool CanEdit => !_busy;
    public bool StartWithWindows { get => _startWithWindows; set { if (!_busy && value != _startWithWindows) ApplyStartup(startup.SetEnabled(value)); } }
    public bool CloseToTray { get => settings.Current.CloseToTray; set { if (!_busy && value != CloseToTray) _pending = SaveCloseAsync(value); } }
    public string? Status { get => _status; private set => SetProperty(ref _status, value); }
    public void Refresh() { ApplyStartup(startup.Read()); OnPropertyChanged(nameof(CloseToTray)); }
    private void ApplyStartup(StartupRegistrationState state)
    {
        _startWithWindows = state.Enabled;
        Status = state.Error;
        OnPropertyChanged(nameof(StartWithWindows));
        if (state.Error is not null) toast.Show(state.Error);
    }
    private async Task SaveCloseAsync(bool value)
    {
        _busy = true; OnPropertyChanged(nameof(CanEdit));
        try { if (!await settings.UpdateAsync(s => s with { CloseToTray = value })) toast.Show("后台驻留设置保存失败，原设置保持不变。"); }
        finally { _busy = false; OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CloseToTray)); }
    }
    public Task FlushAsync() => _pending;
}
