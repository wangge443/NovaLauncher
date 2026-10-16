using System.Collections.ObjectModel;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.ComponentModel;
using System.Windows.Data;
using NovaLauncher.App.Services;

namespace NovaLauncher.App.ViewModels;

public enum WorkspaceMode { Home, Category, Favorites, Recent }

public sealed class ContentViewModel : ViewModelBase, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _token;
    private readonly IApplicationImportService _importer;
    private readonly IApplicationLaunchService _launcher;
    private readonly IApplicationPersistenceService _persistence;
    private Task? _initialization;
    private Task _importTask = Task.CompletedTask;
    private readonly HashSet<string> _targets = new(StringComparer.OrdinalIgnoreCase);
    private bool _isDragOver;
    private bool _isImporting;
    private string? _statusMessage;
    private int _messageVersion;

    public ContentViewModel(IApplicationImportService importer, IApplicationLaunchService launcher,
        IApplicationPersistenceService persistence, IToastService toast, ICategoryService? categories = null)
    {
        _importer = importer;
        _launcher = launcher;
        _persistence = persistence;
        Toast = toast;
        _token = _lifetime.Token;
        CategoryManager = new(categories, toast);
        FilteredApplications = new ListCollectionView(Applications);
        FilteredApplications.Filter = MatchesMode;
    }

    private bool MatchesMode(object item) => Mode switch
        {
            WorkspaceMode.Category => ((ApplicationCardViewModel)item).CategoryId == SelectedCategory?.Id,
            WorkspaceMode.Favorites => ((ApplicationCardViewModel)item).IsFavorite,
            WorkspaceMode.Recent => ((ApplicationCardViewModel)item).LastLaunchTime.HasValue,
            _ => true
        };

    public ObservableCollection<ApplicationCardViewModel> Applications { get; } = [];
    public ICollectionView FilteredApplications { get; }
    public CategoryManagerViewModel CategoryManager { get; }
    private CategoryViewModel? _selectedCategory;
    public CategoryViewModel? SelectedCategory
    {
        get => _selectedCategory;
        private set => SetProperty(ref _selectedCategory, value);
    }
    private WorkspaceMode _mode;
    public WorkspaceMode Mode { get => _mode; private set => SetProperty(ref _mode, value); }
    public void SelectWorkspace(WorkspaceMode mode, CategoryViewModel? category = null)
    {
        if (mode == WorkspaceMode.Category && category is null) throw new ArgumentNullException(nameof(category));
        SelectedCategory = mode == WorkspaceMode.Category ? category : null;
        Mode = mode;
        using (FilteredApplications.DeferRefresh())
        {
            ((ListCollectionView)FilteredApplications).CustomSort = mode == WorkspaceMode.Recent ? RecentOrder.Instance : null;
            FilteredApplications.Filter = MatchesMode;
        }
        NotifyEmptyState();
    }
    public string EmptyTitle => Mode switch
    {
        WorkspaceMode.Favorites => "还没有收藏应用",
        WorkspaceMode.Recent => "最近还没有启动过应用",
        WorkspaceMode.Category => "此分类暂无应用",
        _ => "请将文件拖拽至此"
    };
    public string EmptyHint => Mode switch
    {
        WorkspaceMode.Favorites => "点击应用卡片上的星标即可收藏",
        WorkspaceMode.Recent => "启动应用后，它会出现在这里",
        WorkspaceMode.Category => "在首页通过卡片上的“分类”添加应用",
        _ => "支持 .exe 和 .lnk"
    };
    private void RefreshFilter()
    {
        FilteredApplications.Refresh();
        NotifyEmptyState();
    }
    private void NotifyEmptyState()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyHint));
    }
    private void RefreshFavorites()
    {
        // Home/category membership is unchanged; keep their controls and keyboard focus intact.
        if (Mode == WorkspaceMode.Favorites) RefreshFilter();
    }
    private void CardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Mode == WorkspaceMode.Recent && e.PropertyName == nameof(ApplicationCardViewModel.LastLaunchTime))
            RefreshFilter();
    }

    private sealed class RecentOrder : IComparer
    {
        public static readonly RecentOrder Instance = new();
        public int Compare(object? x, object? y)
        {
            var left = (ApplicationCardViewModel)x!;
            var right = (ApplicationCardViewModel)y!;
            int time = Nullable.Compare(right.LastLaunchTime, left.LastLaunchTime);
            if (time != 0) return time;
            int name = StringComparer.OrdinalIgnoreCase.Compare(left.DisplayName, right.DisplayName);
            return name != 0 ? name : left.Application.Id.CompareTo(right.Application.Id);
        }
    }
    public IToastService Toast { get; }
    public bool IsEmpty => FilteredApplications.IsEmpty;
    public bool IsDragOver { get => _isDragOver; private set => SetProperty(ref _isDragOver, value); }
    public bool IsImporting { get => _isImporting; private set => SetProperty(ref _isImporting, value); }
    public string? StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    public Task InitializeAsync() => _initialization ??= LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            StatusMessage = "正在加载应用…";
            var result = await _persistence.LoadAsync(_token);
            _token.ThrowIfCancellationRequested();
            foreach (var application in result.Applications) AddCard(application);
            await CategoryManager.InitializeAsync(_token);
            if (result.Error is { } error) ShowMessage(error);
            else StatusMessage = null;
        }
        catch (OperationCanceledException) when (_token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Trace.TraceError("Application loading failed: {0}", ex.GetType().Name);
            ShowMessage("无法加载应用");
        }
    }

    private void AddCard(Models.LauncherApplication application)
    {
        if (_targets.Contains(application.ExecutablePath)) return;
        var card = new ApplicationCardViewModel(application, _launcher, _persistence, CategoryManager, Toast);
        card.CategoryChanged += RefreshFilter;
        card.FavoriteChanged += RefreshFavorites;
        card.PropertyChanged += CardPropertyChanged;
        card.LaunchFailed += ShowMessage;
        _targets.Add(application.ExecutablePath);
        Applications.Add(card);
        OnPropertyChanged(nameof(IsEmpty));
    }

    public async Task FlushAsync()
    {
        if (_initialization is not null) await _initialization;
        await _importTask;
        await (CategoryManager.CreateCommand.ExecutionTask ?? Task.CompletedTask);
        await Task.WhenAll(Applications.Select(card => card.ChangeCategoryCommand.ExecutionTask ?? Task.CompletedTask));
        await Task.WhenAll(Applications.Select(card => card.ToggleFavoriteCommand.ExecutionTask ?? Task.CompletedTask));
        await Task.WhenAll(Applications.Select(card => card.LaunchCommand.ExecutionTask ?? Task.CompletedTask));
        await _persistence.FlushAsync();
    }

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

    public Task ImportAsync(IEnumerable<string> paths)
    {
        if (IsImporting || _token.IsCancellationRequested) return Task.CompletedTask;
        return _importTask = ImportCoreAsync(paths);
    }

    private async Task ImportCoreAsync(IEnumerable<string> paths)
    {
        EndDrag();
        if (IsImporting || _token.IsCancellationRequested) return;
        IsImporting = true;
        _messageVersion++;
        StatusMessage = "正在导入…";
        var notices = new HashSet<string>();
        try
        {
            await InitializeAsync();
            _token.ThrowIfCancellationRequested();
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
                    Toast.Show("该应用已添加");
                    continue;
                }
                string? error = await _persistence.AddAsync(application, _token);
                _token.ThrowIfCancellationRequested();
                if (error == "该应用已添加") { Toast.Show(error); continue; }
                if (error is not null) { notices.Add(error); continue; }
                AddCard(application);
            }
            if (notices.Count > 0) ShowMessage(string.Join("；", notices));
            else StatusMessage = null;
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
        Toast.Dispose();
        _lifetime.Dispose();
        foreach (var card in Applications)
        {
            card.LaunchFailed -= ShowMessage;
            card.CategoryChanged -= RefreshFilter;
            card.FavoriteChanged -= RefreshFavorites;
            card.PropertyChanged -= CardPropertyChanged;
            card.Dispose();
        }
    }
}
