using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using NovaLauncher.App.Services;

namespace NovaLauncher.App.ViewModels;

public sealed partial class ApplicationCardViewModel
{
    private IApplicationManagementService? _management;
    private IApplicationIconPicker? _iconPicker;
    private bool _isManagementBusy;
    private bool _isRemoving;
    private string _renameDraft = "";
    private string? _renameError;
    private bool CanInteract => !_token.IsCancellationRequested && !_isRemoving;
    public bool CanManage => CanInteract && !IsManagementBusy && _management is not null;
    public bool HasCustomIcon => Application.CustomIconPath is not null;
    public string CustomIconStatus => HasCustomIcon ? "是" : "否";
    public event Action<ApplicationCardViewModel>? Removed;
    public bool IsManagementBusy
    {
        get => _isManagementBusy;
        private set { SetProperty(ref _isManagementBusy, value); NotifyManagementCommands(); }
    }
    public string RenameDraft
    {
        get => _renameDraft;
        set { SetProperty(ref _renameDraft, value); RenameError = ApplicationManagementService.ValidateName(value); RenameCommand.NotifyCanExecuteChanged(); }
    }
    public string? RenameError { get => _renameError; private set => SetProperty(ref _renameError, value); }
    public IRelayCommand BeginRenameCommand { get; private set; } = null!;
    public IAsyncRelayCommand RenameCommand { get; private set; } = null!;
    public IAsyncRelayCommand ChangeIconCommand { get; private set; } = null!;
    public IAsyncRelayCommand RestoreIconCommand { get; private set; } = null!;
    public IAsyncRelayCommand RemoveCommand { get; private set; } = null!;

    private void InitializeManagement(IApplicationManagementService? management, IApplicationIconPicker? picker)
    {
        _management = management; _iconPicker = picker;
        BeginRenameCommand = new RelayCommand(() => { RenameDraft = DisplayName; RenameError = null; }, () => CanManage);
        RenameCommand = new AsyncRelayCommand(RenameAsync, () => CanManage && ApplicationManagementService.ValidateName(RenameDraft) is null);
        ChangeIconCommand = new AsyncRelayCommand(ChangeIconAsync, () => CanManage && _iconPicker is not null);
        RestoreIconCommand = new AsyncRelayCommand(RestoreIconAsync, () => CanManage && HasCustomIcon);
        RemoveCommand = new AsyncRelayCommand(RemoveAsync, () => CanManage);
    }

    private void NotifyManagementCommands()
    {
        OnPropertyChanged(nameof(CanManage));
        BeginRenameCommand.NotifyCanExecuteChanged(); RenameCommand.NotifyCanExecuteChanged();
        ChangeIconCommand.NotifyCanExecuteChanged(); RestoreIconCommand.NotifyCanExecuteChanged(); RemoveCommand.NotifyCanExecuteChanged();
        LaunchCommand.NotifyCanExecuteChanged(); ToggleFavoriteCommand.NotifyCanExecuteChanged(); ChangeCategoryCommand.NotifyCanExecuteChanged();
    }

    private async Task RenameAsync()
    {
        if (!CanManage || ApplicationManagementService.ValidateName(RenameDraft) is not null) return;
        IsManagementBusy = true;
        string name = RenameDraft.Trim();
        try
        {
            RenameError = await _management!.RenameAsync(Application.Id, name);
            if (RenameError is not null) { _toast?.Show(RenameError); return; }
            Application = Application with { DisplayName = name };
            OnPropertyChanged(nameof(Application)); OnPropertyChanged(nameof(DisplayName)); OnPropertyChanged(nameof(FavoriteActionName));
        }
        catch (Exception ex) { Trace.TraceError("Rename command failed: {0}", ex); RenameError = "名称保存失败"; _toast?.Show(RenameError); }
        finally { IsManagementBusy = false; }
    }

    private void ApplyIcon(ApplicationIconResult result)
    {
        if (!result.Succeeded) { _toast?.Show(result.Error ?? "图标保存失败"); return; }
        var icon = DecodeIcon(result.Icon);
        Application = Application with { CustomIconPath = result.Path, Icon = result.Icon };
        Icon = icon;
        OnPropertyChanged(nameof(Application)); OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(HasCustomIcon)); OnPropertyChanged(nameof(CustomIconStatus));
    }

    private async Task ChangeIconAsync()
    {
        if (!CanManage || _iconPicker is null) return;
        IsManagementBusy = true;
        try
        {
            string? source = _iconPicker.Pick();
            if (source is not null) ApplyIcon(await _management!.ChangeIconAsync(Application, source));
        }
        catch (Exception ex) { Trace.TraceWarning("Choose icon failed: {0}", ex); _toast?.Show("无法读取该图片"); }
        finally { IsManagementBusy = false; }
    }

    private async Task RestoreIconAsync()
    {
        if (!CanManage || !HasCustomIcon) return;
        IsManagementBusy = true;
        try { ApplyIcon(await _management!.RestoreIconAsync(Application)); }
        catch (Exception ex) { Trace.TraceError("Restore icon command failed: {0}", ex); _toast?.Show("图标保存失败"); }
        finally { IsManagementBusy = false; }
    }

    private async Task RemoveAsync()
    {
        if (!CanManage) return;
        _isRemoving = true; IsManagementBusy = true;
        try
        {
            // Let existing writes finish; new operations are disabled across every shared card view.
            await Task.WhenAll(LaunchCommand.ExecutionTask ?? Task.CompletedTask,
                ToggleFavoriteCommand.ExecutionTask ?? Task.CompletedTask, ChangeCategoryCommand.ExecutionTask ?? Task.CompletedTask);
            string? error = await _management!.RemoveAsync(Application);
            if (error is not null) { _toast?.Show(error); return; }
            Removed?.Invoke(this);
        }
        catch (Exception ex) { Trace.TraceError("Remove command failed: {0}", ex); _toast?.Show("无法移除该应用"); }
        finally { _isRemoving = false; IsManagementBusy = false; }
    }

    public Task FlushManagementAsync() => Task.WhenAll(RenameCommand.ExecutionTask ?? Task.CompletedTask,
        ChangeIconCommand.ExecutionTask ?? Task.CompletedTask, RestoreIconCommand.ExecutionTask ?? Task.CompletedTask,
        RemoveCommand.ExecutionTask ?? Task.CompletedTask);
}
