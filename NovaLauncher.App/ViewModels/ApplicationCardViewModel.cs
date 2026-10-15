using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NovaLauncher.App.Models;
using NovaLauncher.App.Services;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;

namespace NovaLauncher.App.ViewModels;

public sealed class ApplicationCardViewModel : ViewModelBase, IDisposable
{
    private readonly IApplicationLaunchService _launcher;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _token;
    private bool _isLaunching;
    public LauncherApplication Application { get; private set; }
    public string DisplayName => Application.DisplayName;
    public ImageSource Icon { get; }
    public long LaunchCount => Application.LaunchCount;
    public DateTimeOffset? LastLaunchTime => Application.LastLaunchTime;
    public string LastLaunchText => $"上次启动：{LaunchTimeFormatter.Format(LastLaunchTime, DateTimeOffset.Now)}";
    public string LaunchCountText => $"启动次数：{LaunchCount}";
    public bool IsLaunching { get => _isLaunching; private set => SetProperty(ref _isLaunching, value); }
    public IAsyncRelayCommand LaunchCommand { get; }
    public event Action<string>? LaunchFailed;

    public ApplicationCardViewModel(LauncherApplication application, IApplicationLaunchService launcher)
    {
        _launcher = launcher;
        _token = _lifetime.Token;
        Application = application;
        LaunchCommand = new AsyncRelayCommand(LaunchAsync, () => !_token.IsCancellationRequested);
        if (application.Icon is { Length: > 0 } bytes)
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            Icon = image;
        }
        else
        {
            Icon = DefaultIcon;
        }
    }

    public void RefreshLaunchTime() => OnPropertyChanged(nameof(LastLaunchText));

    private async Task LaunchAsync()
    {
        // Also guard direct ExecuteAsync calls, not just the Button's CanExecute path.
        if (IsLaunching || _token.IsCancellationRequested) return;
        IsLaunching = true;
        long started = Stopwatch.GetTimestamp();
        try
        {
            var result = await _launcher.LaunchAsync(Application, _token);
            if (_token.IsCancellationRequested) return;
            if (result.Succeeded)
            {
                Application = Application with { LaunchCount = LaunchCount + 1, LastLaunchTime = DateTimeOffset.Now };
                OnPropertyChanged(nameof(Application));
                OnPropertyChanged(nameof(LaunchCount));
                OnPropertyChanged(nameof(LastLaunchTime));
                OnPropertyChanged(nameof(LaunchCountText));
                RefreshLaunchTime();
            }
            else LaunchFailed?.Invoke(result.Error ?? "无法启动该应用");
        }
        catch (OperationCanceledException) when (_token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Trace.TraceWarning("Launch command failed: {0}", ex);
            if (!_token.IsCancellationRequested) LaunchFailed?.Invoke("无法启动该应用");
        }
        finally
        {
            TimeSpan remaining = TimeSpan.FromMilliseconds(400) - Stopwatch.GetElapsedTime(started);
            try { if (remaining > TimeSpan.Zero) await Task.Delay(remaining, _token); }
            catch (OperationCanceledException) when (_token.IsCancellationRequested) { }
            IsLaunching = false;
        }
    }

    public void Dispose()
    {
        if (_token.IsCancellationRequested) return;
        _lifetime.Cancel();
        _lifetime.Dispose();
        LaunchCommand.NotifyCanExecuteChanged();
    }

    // Vector fallback stays sharp at any DPI and never owns a native resource.
    private static readonly ImageSource DefaultIcon = CreateDefaultIcon();

    private static ImageSource CreateDefaultIcon()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(82, 107, 181)), null,
            new RectangleGeometry(new Rect(0, 0, 72, 72), 16, 16)));
        group.Children.Add(new GeometryDrawing(null, new Pen(Brushes.White, 5),
            Geometry.Parse("M 23,51 L 23,21 49,51 49,21")));
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }
}
