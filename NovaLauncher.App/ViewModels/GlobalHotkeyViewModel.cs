using CommunityToolkit.Mvvm.Input;
using NovaLauncher.App.Models;
using NovaLauncher.App.Services;

namespace NovaLauncher.App.ViewModels;

public sealed class GlobalHotkeyViewModel : ViewModelBase
{
    private readonly GlobalHotkeyService _service;
    private bool _capturing;
    private bool _busy;
    private string? _error;
    private Task _pending = Task.CompletedTask;
    public GlobalHotkeyViewModel(GlobalHotkeyService service)
    {
        _service = service;
        service.PropertyChanged += (_, _) => Refresh();
        BeginCaptureCommand = new RelayCommand(() => { Error = null; IsCapturing = true; });
        CancelCaptureCommand = new RelayCommand(CancelCapture);
        RetryCommand = new AsyncRelayCommand(() => _pending = ChangeAsync(Enabled, service.Hotkey));
    }
    public bool Enabled { get => _service.Enabled; set { if (CanEdit && value != Enabled) _pending = ChangeAsync(value, _service.Hotkey); } }
    public bool IsCapturing
    {
        get => _capturing;
        private set { if (SetProperty(ref _capturing, value)) { _service.SuppressActivation = value; OnPropertyChanged(nameof(CaptureLabel)); } }
    }
    public bool CanEdit => !_busy && _service.IsReady;
    public string Shortcut => _service.Hotkey.ToString();
    public string CaptureLabel => IsCapturing ? "请按新的快捷键（Esc 取消）" : Shortcut;
    public string Status => _service.Status;
    public bool CanRetry => _service.Enabled && !_service.IsRegistered;
    public string? Error { get => _error; private set => SetProperty(ref _error, value); }
    public IRelayCommand BeginCaptureCommand { get; }
    public IRelayCommand CancelCaptureCommand { get; }
    public IAsyncRelayCommand RetryCommand { get; }
    public void CancelCapture() { IsCapturing = false; Error = null; }
    public Task CaptureAsync(GlobalHotkey hotkey)
    {
        if (!IsCapturing || !CanEdit) return Task.CompletedTask;
        return _pending = ChangeAsync(Enabled, hotkey);
    }
    private async Task ChangeAsync(bool enabled, GlobalHotkey hotkey)
    {
        if (!CanEdit) return;
        _busy = true; Refresh();
        try
        {
            if (await _service.ConfigureAsync(enabled, hotkey)) CancelCapture();
            else Error = _service.Status;
        }
        finally { _busy = false; Refresh(); }
    }
    public Task FlushAsync() => _pending;
    private void Refresh()
    {
        OnPropertyChanged(nameof(Enabled)); OnPropertyChanged(nameof(Shortcut)); OnPropertyChanged(nameof(CaptureLabel));
        OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanRetry));
    }
}
