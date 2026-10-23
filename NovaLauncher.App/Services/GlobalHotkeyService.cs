using NovaLauncher.App.Models;
using NovaLauncher.App.ViewModels;
using NovaLauncher.App.Diagnostics;

namespace NovaLauncher.App.Services;

public sealed class GlobalHotkeyService(IAppSettingsService settings, NovaLog? log = null) : ViewModelBase, IDisposable
{
    private IHotkeyRegistrar? _registrar;
    private int? _activeId;
    private bool _busy;
    private bool _disposed;
    private string _status = "等待窗口就绪";
    public GlobalHotkey Hotkey => new(settings.Current.GlobalHotkeyModifiers, settings.Current.GlobalHotkeyKey);
    public bool Enabled => settings.Current.GlobalHotkeyEnabled;
    public bool IsRegistered => _activeId.HasValue;
    public bool IsReady => _registrar is not null && !_disposed;
    public bool SuppressActivation { get; set; }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public event Action? Triggered;

    public void Initialize(IHotkeyRegistrar registrar)
    {
        if (_registrar is not null || _disposed) throw new InvalidOperationException("Hotkey service already initialized or disposed.");
        _registrar = registrar; registrar.Pressed += OnPressed;
        Log($"Startup enabled={Enabled}, shortcut={Hotkey}");
        if (Enabled && TryRegister(1, Hotkey)) _activeId = 1;
        if (!Enabled) Status = "已关闭";
        Notify();
    }
    private bool TryRegister(int id, GlobalHotkey hotkey)
    {
        if (_registrar!.Register(id, hotkey, out int error))
        { Log($"Register success: {hotkey}"); Status = "已启用"; return true; }
        Log($"Register failure code={error}, shortcut={hotkey}", true);
        Status = error == 1409 ? $"快捷键 {hotkey} 已被其他程序占用，请修改快捷键。" : $"快捷键注册失败（{error}），请修改快捷键或重试。";
        if (_activeId.HasValue) Status += "原快捷键仍有效。";
        return false;
    }
    public async Task<bool> ConfigureAsync(bool enabled, GlobalHotkey hotkey)
    {
        if (_busy || !IsReady) return false;
        if (hotkey.ValidationError is { } error) { Status = error; return false; }
        _busy = true;
        int? candidate = null;
        int? previous = _activeId;
        try
        {
            // Hold the old reservation through validation AND persistence. Failure cannot lose it.
            if ((enabled || hotkey != Hotkey) && !(previous.HasValue && hotkey == Hotkey))
            {
                candidate = previous == 1 ? 2 : 1;
                if (!TryRegister(candidate.Value, hotkey))
                { candidate = null; return false; }
            }
            if (!await settings.UpdateAsync(s => s with
            { GlobalHotkeyEnabled = enabled, GlobalHotkeyModifiers = hotkey.Modifiers, GlobalHotkeyKey = hotkey.Key }))
            { Status = "快捷键设置保存失败，原配置保持不变。"; return false; }
            if (_disposed) return false;
            if (candidate.HasValue || !enabled)
            {
                if (previous.HasValue) Release(previous.Value);
                _activeId = enabled ? candidate : null;
            }
            if (enabled) candidate = null; // The candidate is now the committed registration.
            Status = enabled ? "已启用" : "已关闭";
            Log($"Configuration changed: enabled={enabled}, shortcut={hotkey}");
            return true;
        }
        finally
        {
            if (candidate.HasValue && !_disposed) Release(candidate.Value);
            _busy = false; Notify();
        }
    }
    private void Log(string message, bool warning = false) => log?.Write(warning ? LogLevel.Warning : LogLevel.Information, "GlobalHotkey", message);
    private void Release(int id) { _registrar!.Unregister(id); Log("Unregister"); }
    private void OnPressed(int id)
    {
        if (id != _activeId || SuppressActivation || _disposed) return;
        Log("Triggered");
        Triggered?.Invoke();
    }
    private void Notify()
    {
        OnPropertyChanged(nameof(Hotkey)); OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(IsRegistered)); OnPropertyChanged(nameof(IsReady));
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_registrar is not null)
        { _registrar.Pressed -= OnPressed; if (_activeId.HasValue) Release(_activeId.Value); _registrar.Dispose(); }
        _activeId = null;
    }
}
