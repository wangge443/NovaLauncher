using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaLauncher.App.Services;

namespace NovaLauncher.App.ViewModels;

public sealed class QuickLauncherViewModel : ViewModelBase, IDisposable
{
    private readonly ObservableCollection<ApplicationCardViewModel> _source;
    private readonly HashSet<ApplicationCardViewModel> _observed = [];
    private readonly ApplicationSearchService _search;
    private bool _isOpen;
    private string _query = "";
    private string? _error;
    private int _selectedIndex = -1;
    private int _session;
    public QuickLauncherViewModel(ObservableCollection<ApplicationCardViewModel> source)
    {
        _source = source;
        // Subscribe the index before result refresh handlers so changes are searchable immediately.
        _search = new(source);
        LaunchCommand = new AsyncRelayCommand<ApplicationCardViewModel>(LaunchAsync);
        OpenCommand = new RelayCommand(Open);
        CloseCommand = new RelayCommand(Close);
        source.CollectionChanged += SourceChanged;
        ObserveSource();
    }
    public ObservableCollection<ApplicationCardViewModel> Results { get; } = [];
    public IRelayCommand OpenCommand { get; }
    public IRelayCommand CloseCommand { get; }
    public IAsyncRelayCommand<ApplicationCardViewModel> LaunchCommand { get; }
    public bool IsOpen { get => _isOpen; private set => SetProperty(ref _isOpen, value); }
    public string SearchQuery
    {
        get => _query;
        set { if (SetProperty(ref _query, value)) { Error = null; Refresh(true); } }
    }
    public int SelectedIndex
    {
        get => _selectedIndex;
        set => SetProperty(ref _selectedIndex, Results.Count == 0 ? -1 : Math.Clamp(value, 0, Results.Count - 1));
    }
    public string? Error { get => _error; private set => SetProperty(ref _error, value); }
    public string Heading => !string.IsNullOrWhiteSpace(SearchQuery) ? "搜索结果"
        : Results.Any(x => x.LastLaunchTime.HasValue) ? "最近使用" : Results.Count > 0 ? "收藏应用" : "快速启动";
    public string EmptyHint => string.IsNullOrWhiteSpace(SearchQuery) ? "输入应用名称开始搜索" : "未找到应用";
    public bool IsEmpty => Results.Count == 0;
    public void Open()
    {
        if (IsOpen) return;
        _session++;
        IsOpen = true;
        Error = null;
        Refresh(true);
    }
    public void Close()
    {
        _session++;
        IsOpen = false;
        SearchQuery = "";
        Error = null;
        Results.Clear();
        SelectedIndex = -1;
    }
    public void MoveSelection(int delta) => SelectedIndex += delta;
    public Task LaunchSelectedAsync() => SelectedIndex >= 0 && SelectedIndex < Results.Count && !LaunchCommand.IsRunning
        ? LaunchCommand.ExecuteAsync(Results[SelectedIndex]) : Task.CompletedTask;
    private async Task LaunchAsync(ApplicationCardViewModel? card)
    {
        if (!IsOpen || card is null || !Results.Contains(card) || !card.LaunchCommand.CanExecute(null)) return;
        int session = _session;
        long previousCount = card.LaunchCount;
        Error = null;
        void Failed(string message) { if (session == _session) Error = message; }
        card.LaunchFailed += Failed;
        try
        {
            await card.LaunchCommand.ExecuteAsync(null);
            if (session == _session && card.LaunchCount > previousCount) Close();
        }
        finally { card.LaunchFailed -= Failed; }
    }
    private void Refresh(bool resetSelection = false)
    {
        if (!IsOpen) return;
        var selected = SelectedIndex >= 0 && SelectedIndex < Results.Count ? Results[SelectedIndex] : null;
        _search.Query = SearchQuery.Trim();
        ApplicationCardViewModel[] desired;
        if (_search.Query.Length > 0)
        {
            var matches = _source.Where(_search.Matches).ToArray();
            Array.Sort(matches, (a, b) => _search.Compare(a, b));
            desired = matches.Take(8).ToArray();
        }
        else
        {
            desired = _source.Where(x => x.LastLaunchTime.HasValue).OrderBy(x => x, RecentApplicationComparer.Instance).Take(6).ToArray();
            if (desired.Length == 0) desired = _source.Where(x => x.IsFavorite).Take(6).ToArray();
        }
        for (int i = Results.Count - 1; i >= 0; i--) if (!desired.Contains(Results[i])) Results.RemoveAt(i);
        for (int i = 0; i < desired.Length; i++)
        {
            int existing = Results.IndexOf(desired[i]);
            if (existing < 0) Results.Insert(i, desired[i]);
            else if (existing != i) Results.Move(existing, i);
        }
        SelectedIndex = resetSelection ? 0 : selected is null ? 0 : Math.Max(0, Results.IndexOf(selected));
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(EmptyHint));
        OnPropertyChanged(nameof(IsEmpty));
    }
    private void ObserveSource()
    {
        foreach (var card in _observed.Where(x => !_source.Contains(x)).ToArray())
        { card.PropertyChanged -= CardChanged; _observed.Remove(card); }
        foreach (var card in _source)
            if (_observed.Add(card)) card.PropertyChanged += CardChanged;
    }
    private void SourceChanged(object? sender, NotifyCollectionChangedEventArgs e) { ObserveSource(); Refresh(); }
    private void CardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ApplicationCardViewModel.DisplayName) or nameof(ApplicationCardViewModel.LastLaunchTime)
            or nameof(ApplicationCardViewModel.IsFavorite)) Refresh();
    }
    public void Dispose()
    {
        _search.Dispose();
        _source.CollectionChanged -= SourceChanged;
        foreach (var card in _observed) card.PropertyChanged -= CardChanged;
        _observed.Clear();
    }
}
