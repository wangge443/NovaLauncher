using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaLauncher.App.Diagnostics;

namespace NovaLauncher.App.ViewModels;

public sealed class HomeViewModel : ViewModelBase, IDisposable
{
    private readonly ObservableCollection<ApplicationCardViewModel> _source;
    private readonly ObservableCollection<ApplicationCardViewModel> _recent = [];
    private readonly ObservableCollection<ApplicationCardViewModel> _favorites = [];
    private readonly HashSet<ApplicationCardViewModel> _observed = [];
    public ReadOnlyObservableCollection<ApplicationCardViewModel> AllApplications { get; }
    public ReadOnlyObservableCollection<ApplicationCardViewModel> RecentApplications { get; }
    public ReadOnlyObservableCollection<ApplicationCardViewModel> FavoriteApplications { get; }
    public bool HasApplications => _source.Count > 0;
    public bool HasRecent => _recent.Count > 0;
    public bool HasFavorites => _favorites.Count > 0;
    public IRelayCommand ShowRecentCommand { get; }
    public IRelayCommand ShowFavoritesCommand { get; }
    public event Action<WorkspaceMode>? NavigationRequested;

    public HomeViewModel(ObservableCollection<ApplicationCardViewModel> source)
    {
        _source = source;
        AllApplications = new(source);
        RecentApplications = new(_recent);
        FavoriteApplications = new(_favorites);
        ShowRecentCommand = new RelayCommand(() => NavigationRequested?.Invoke(WorkspaceMode.Recent));
        ShowFavoritesCommand = new RelayCommand(() => NavigationRequested?.Invoke(WorkspaceMode.Favorites));
        _source.CollectionChanged += SourceChanged;
        SynchronizeSubscriptions();
        RefreshRecent();
        RefreshFavorites();
    }

    private void SourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        long phase = PerformanceTrace.Start();
        SynchronizeSubscriptions();
        RefreshRecent();
        RefreshFavorites();
        OnPropertyChanged(nameof(HasApplications));
        PerformanceTrace.End("HomeProjection", phase);
    }

    private void SynchronizeSubscriptions()
    {
        var current = _source.ToHashSet();
        foreach (var card in _observed.Where(card => !current.Contains(card)).ToArray())
        {
            card.PropertyChanged -= CardChanged;
            _observed.Remove(card);
        }
        foreach (var card in current)
            if (_observed.Add(card)) card.PropertyChanged += CardChanged;
    }

    private void CardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName)) { RefreshRecent(); RefreshFavorites(); }
        else if (e.PropertyName is nameof(ApplicationCardViewModel.LastLaunchTime) or nameof(ApplicationCardViewModel.DisplayName)) RefreshRecent();
        else if (e.PropertyName == nameof(ApplicationCardViewModel.IsFavorite)) RefreshFavorites();
    }

    private void RefreshRecent()
    {
        Synchronize(_recent, _source.Where(card => card.LastLaunchTime.HasValue)
            .OrderBy(card => card, RecentApplicationComparer.Instance).Take(6).ToArray());
        OnPropertyChanged(nameof(HasRecent));
    }

    private void RefreshFavorites()
    {
        Synchronize(_favorites, _source.Where(card => card.IsFavorite).Take(6).ToArray());
        OnPropertyChanged(nameof(HasFavorites));
    }

    // Keep unchanged controls alive and move existing references; never clone cards or reset entire sections.
    private static void Synchronize(ObservableCollection<ApplicationCardViewModel> target, ApplicationCardViewModel[] desired)
    {
        for (int i = target.Count - 1; i >= 0; i--)
            if (!desired.Contains(target[i])) target.RemoveAt(i);
        for (int i = 0; i < desired.Length; i++)
        {
            int existing = target.IndexOf(desired[i]);
            if (existing < 0) target.Insert(i, desired[i]);
            else if (existing != i) target.Move(existing, i);
        }
    }

    public void Dispose()
    {
        _source.CollectionChanged -= SourceChanged;
        foreach (var card in _observed) card.PropertyChanged -= CardChanged;
        _observed.Clear();
        NavigationRequested = null;
    }
}
