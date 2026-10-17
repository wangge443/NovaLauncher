using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using NovaLauncher.App.Services;

namespace NovaLauncher.App.ViewModels;

public sealed class UserProfileViewModel : ViewModelBase
{
    private readonly IUserProfileService? _service;
    private readonly IAvatarFilePicker _picker;
    private readonly IToastService _toast;
    private Task _pending = Task.CompletedTask;
    private Task? _initialization;
    private string _displayName = "User";
    private string _draftName = "User";
    private string? _error;
    private ImageSource? _avatar;
    private bool _custom, _open, _editing, _busy;
    public string DisplayName { get => _displayName; private set => SetProperty(ref _displayName, value); }
    public string DraftName { get => _draftName; set => SetProperty(ref _draftName, value); }
    public string? Error { get => _error; private set => SetProperty(ref _error, value); }
    public ImageSource? AvatarSource { get => _avatar; private set => SetProperty(ref _avatar, value); }
    public bool HasCustomAvatar { get => _custom; private set => SetProperty(ref _custom, value); }
    public bool IsProfileFlyoutOpen
    {
        get => _open;
        set { if (SetProperty(ref _open, value) && !value) CancelName(); }
    }
    public bool IsEditingName { get => _editing; private set => SetProperty(ref _editing, value); }
    public bool IsBusy
    {
        get => _busy;
        private set
        {
            SetProperty(ref _busy, value);
            SaveNameCommand.NotifyCanExecuteChanged(); ChangeAvatarCommand.NotifyCanExecuteChanged();
            ResetAvatarCommand.NotifyCanExecuteChanged(); BeginEditNameCommand.NotifyCanExecuteChanged();
        }
    }
    public IRelayCommand ToggleProfileFlyoutCommand { get; }
    public IRelayCommand CloseProfileFlyoutCommand { get; }
    public IRelayCommand BeginEditNameCommand { get; }
    public IRelayCommand CancelEditNameCommand { get; }
    public IAsyncRelayCommand SaveNameCommand { get; }
    public IAsyncRelayCommand ChangeAvatarCommand { get; }
    public IAsyncRelayCommand ResetAvatarCommand { get; }
    public IRelayCommand OpenSettingsCommand { get; }
    public event Action? SettingsRequested;
    public UserProfileViewModel(IUserProfileService? service, IAvatarFilePicker picker, IToastService toast)
    {
        _service = service; _picker = picker; _toast = toast;
        ToggleProfileFlyoutCommand = new RelayCommand(() => IsProfileFlyoutOpen = !IsProfileFlyoutOpen);
        CloseProfileFlyoutCommand = new RelayCommand(() => IsProfileFlyoutOpen = false);
        BeginEditNameCommand = new RelayCommand(() => { DraftName = DisplayName; Error = null; IsEditingName = true; }, () => !IsBusy);
        CancelEditNameCommand = new RelayCommand(CancelName);
        SaveNameCommand = new AsyncRelayCommand(() => TrackAsync(async () =>
        {
            var result = await _service!.SaveNameAsync(DraftName);
            if (Apply(result)) IsEditingName = false;
            else if (result.Error == "名称保存失败") DraftName = DisplayName;
        }), () => !IsBusy && _service is not null);
        ChangeAvatarCommand = new AsyncRelayCommand(() => TrackAsync(async () =>
        {
            string? path = _picker.Pick();
            if (path is not null) Apply(await _service!.ChangeAvatarAsync(path));
        }), () => !IsBusy && _service is not null);
        ResetAvatarCommand = new AsyncRelayCommand(() => TrackAsync(async () => Apply(await _service!.ResetAvatarAsync())), () => !IsBusy && _service is not null);
        OpenSettingsCommand = new RelayCommand(() => { IsProfileFlyoutOpen = false; SettingsRequested?.Invoke(); });
    }
    public Task InitializeAsync() => _initialization ??= TrackAsync(async () =>
    {
        if (_service is not null) Apply(await _service.LoadAsync());
    });
    private Task TrackAsync(Func<Task> operation) => _pending = RunAsync(operation);
    private async Task RunAsync(Func<Task> operation)
    {
        IsBusy = true; Error = null;
        try { await operation(); }
        catch (Exception ex) { Trace.TraceError("Profile operation failed: {0}", ex); Error = "个人资料操作失败"; _toast.Show(Error); }
        finally { IsBusy = false; }
    }
    private bool Apply(ProfileResult result)
    {
        if (result.Error is { } error) { Error = error; _toast.Show(error); return false; }
        if (result.Profile is not { } profile) return false;
        ImageSource? image = null;
        if (result.Avatar is { Length: > 0 } bytes)
        {
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
            image = bitmap;
        }
        DisplayName = profile.DisplayName; AvatarSource = image; HasCustomAvatar = profile.AvatarPath is not null;
        Error = null;
        return true;
    }
    private void CancelName() { DraftName = DisplayName; IsEditingName = false; Error = null; }
    public async Task FlushAsync() { if (_initialization is not null) await _initialization; await _pending; }
}
