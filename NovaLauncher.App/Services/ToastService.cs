using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NovaLauncher.App.Services;

public interface IToastService : INotifyPropertyChanged, IDisposable
{
    string? Message { get; }
    bool IsOpen { get; }
    void Show(string message);
}

// Scoped to a workspace; callers use its UI synchronization context, as with bound collections.
public sealed class ToastService : ObservableObject, IToastService
{
    private CancellationTokenSource? _dismiss;
    private string? _message;
    private bool _isOpen;
    private bool _disposed;
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public bool IsOpen { get => _isOpen; private set => SetProperty(ref _isOpen, value); }

    public void Show(string message)
    {
        if (_disposed || string.IsNullOrWhiteSpace(message)) return;
        _dismiss?.Cancel();
        _dismiss?.Dispose();
        _dismiss = new CancellationTokenSource();
        Message = message;
        IsOpen = true;
        _ = DismissAsync(_dismiss.Token);
    }

    private async Task DismissAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), token);
            IsOpen = false;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        _disposed = true;
        _dismiss?.Cancel();
        _dismiss?.Dispose();
        _dismiss = null;
        IsOpen = false;
    }
}
