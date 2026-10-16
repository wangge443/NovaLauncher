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
    private readonly IApplicationPersistenceService _persistence;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _token;
    private bool _isLaunching;
    public LauncherApplication Application { get; private set; }
    public string DisplayName => Application.DisplayName;
    public bool IsAvailable => Application.IsAvailable;
    public ImageSource Icon { get; }
    public long LaunchCount => Application.LaunchCount;
    public DateTimeOffset? LastLaunchTime => Application.LastLaunchTime;
    public string LastLaunchText => $"上次启动：{LaunchTimeFormatter.Format(LastLaunchTime, DateTimeOffset.Now)}";
    public string LaunchCountText => $"启动次数：{LaunchCount}";
    public bool IsLaunching { get => _isLaunching; private set => SetProperty(ref _isLaunching, value); }
    public IAsyncRelayCommand LaunchCommand { get; }
    public event Action<string>? LaunchFailed;
    public event Action? CategoryChanged;
    public Guid? CategoryId => Application.CategoryId;
    public CategoryChoice? CurrentCategoryChoice => CategoryManager?.Choices.FirstOrDefault(x => x.Id == CategoryId);
    public CategoryManagerViewModel? CategoryManager { get; }
    private readonly IToastService? _toast;
    private bool _isCategoryPickerOpen;
    private bool _isChangingCategory;
    public bool IsChangingCategory { get => _isChangingCategory; private set => SetProperty(ref _isChangingCategory, value); }
    public bool IsCategoryPickerOpen
    {
        get => _isCategoryPickerOpen;
        set { SetProperty(ref _isCategoryPickerOpen, value); OnPropertyChanged(nameof(CurrentCategoryChoice)); }
    }
    public IAsyncRelayCommand<CategoryChoice> ChangeCategoryCommand { get; }

    public ApplicationCardViewModel(LauncherApplication application, IApplicationLaunchService launcher,
        IApplicationPersistenceService persistence, CategoryManagerViewModel? categoryManager = null, IToastService? toast = null)
    {
        _launcher = launcher;
        _persistence = persistence;
        CategoryManager = categoryManager;
        _toast = toast;
        ChangeCategoryCommand = new AsyncRelayCommand<CategoryChoice>(ChangeCategoryAsync);
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

    private async Task ChangeCategoryAsync(CategoryChoice? choice)
    {
        if (choice is null || IsChangingCategory || _token.IsCancellationRequested) return;
        if (choice.Id == CategoryId) { IsCategoryPickerOpen = false; return; }
        IsChangingCategory = true;
        try
        {
            string? error = await _persistence.SetCategoryAsync(Application.Id, choice.Id, CancellationToken.None);
            if (error is null)
            {
                Application = Application with { CategoryId = choice.Id };
                OnPropertyChanged(nameof(Application));
                OnPropertyChanged(nameof(CategoryId));
                OnPropertyChanged(nameof(CurrentCategoryChoice));
                IsCategoryPickerOpen = false;
                CategoryChanged?.Invoke();
            }
            else _toast?.Show(error);
        }
        catch (Exception ex) { Trace.TraceError("Category change failed: {0}", ex); _toast?.Show("分类修改失败"); }
        finally { IsChangingCategory = false; OnPropertyChanged(nameof(CategoryId)); OnPropertyChanged(nameof(CurrentCategoryChoice)); }
    }

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
                // Persistence has a separate error boundary: a launched application remains a success.
                try { await _persistence.SaveStatisticsAsync(Application); }
                catch (Exception ex) { Trace.TraceError("Statistics persistence failed: {0}", ex.GetType().Name); }
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
