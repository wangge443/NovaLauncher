using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using NovaLauncher.App.Services;

namespace NovaLauncher.App.ViewModels;

public sealed class ContentViewModel : ViewModelBase, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _token;
    private readonly IApplicationImportService _importer;
    private readonly HashSet<string> _targets = new(StringComparer.OrdinalIgnoreCase);
    private bool _isDragOver;
    private bool _isImporting;
    private string? _statusMessage;
    private int _messageVersion;

    public ContentViewModel(IApplicationImportService importer)
    {
        _importer = importer;
        _token = _lifetime.Token;
    }

    public ObservableCollection<ApplicationCardViewModel> Applications { get; } = [];
    public bool IsEmpty => Applications.Count == 0;
    public bool IsDragOver { get => _isDragOver; private set => SetProperty(ref _isDragOver, value); }
    public bool IsImporting { get => _isImporting; private set => SetProperty(ref _isImporting, value); }
    public string? StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    public bool PreviewDrop(IEnumerable<string> paths)
    {
        bool supported = paths.Any(path =>
            string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase));
        IsDragOver = supported && !IsImporting;
        if (!IsDragOver)
        {
            string message = IsImporting ? "正在导入，请稍候" : "不支持此文件类型";
            if (StatusMessage != message) ShowMessage(message);
        }
        return IsDragOver;
    }

    public void EndDrag() => IsDragOver = false;

    public async Task ImportAsync(IEnumerable<string> paths)
    {
        EndDrag();
        if (IsImporting || _token.IsCancellationRequested) return;
        IsImporting = true;
        _messageVersion++;
        StatusMessage = "正在导入…";
        int added = 0;
        var notices = new HashSet<string>();
        try
        {
            foreach (string path in paths)
            {
                var result = await _importer.ImportAsync(path, _token);
                _token.ThrowIfCancellationRequested();
                if (result.Application is not { } application)
                {
                    notices.Add(result.Error ?? "无法导入此文件");
                    continue;
                }
                if (_targets.Contains(application.ExecutablePath))
                {
                    notices.Add("该应用已添加");
                    continue;
                }
                var card = new ApplicationCardViewModel(application);
                _targets.Add(application.ExecutablePath);
                Applications.Add(card);
                OnPropertyChanged(nameof(IsEmpty));
                added++;
            }
            if (added > 0) notices.Add($"已添加 {added} 个应用");
            ShowMessage(string.Join("；", notices));
        }
        catch (OperationCanceledException) when (_token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Trace.TraceWarning("Import presentation failed: {0}", ex.GetType().Name);
            ShowMessage("无法完成导入，请重试");
        }
        finally { IsImporting = false; }
    }

    private void ShowMessage(string message)
    {
        int version = ++_messageVersion;
        StatusMessage = message;
        _ = ClearMessageAsync(version);
    }

    private async Task ClearMessageAsync(int version)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(4), _token);
            if (version == _messageVersion) StatusMessage = null;
        }
        catch (OperationCanceledException) when (_token.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        if (_token.IsCancellationRequested) return;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
