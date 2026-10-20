using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NovaLauncher.App.ViewModels;

namespace NovaLauncher.App.Views;

public partial class HomeView : UserControl
{
    private readonly ObservableCollection<ApplicationCardViewModel> _rendered = [];
    private HomeViewModel? _home;
    private DispatcherOperation? _pending;
    public bool IsFullyRendered => _home is null || _rendered.Count == _home.AllApplications.Count;

    public HomeView()
    {
        InitializeComponent();
        AllApplications.ItemsSource = _rendered;
        DataContextChanged += (_, _) => Attach();
        Loaded += (_, _) => { Attach(); Schedule(); };
        Unloaded += (_, _) => Detach();
    }

    private void Attach()
    {
        if (ReferenceEquals(_home, DataContext)) return;
        Detach();
        _home = DataContext as HomeViewModel;
        _rendered.Clear();
        if (_home is null) return;
        ((INotifyCollectionChanged)_home.AllApplications).CollectionChanged += SourceChanged;
        int initial = _home.AllApplications.Count <= 100 ? _home.AllApplications.Count : 40;
        foreach (var card in _home.AllApplications.Take(initial)) _rendered.Add(card);
        Schedule();
    }

    private void Detach()
    {
        _pending?.Abort();
        _pending = null;
        if (_home is not null)
            ((INotifyCollectionChanged)_home.AllApplications).CollectionChanged -= SourceChanged;
        _home = null;
    }

    private void SourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_home is null) return;
        // Keep existing containers, order and object identity. A change never rebuilds ViewModels.
        int desired = Math.Min(_home.AllApplications.Count, Math.Max(_rendered.Count,
            _home.AllApplications.Count <= 100 ? _home.AllApplications.Count : 40));
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewStartingIndex <= _rendered.Count)
            desired = Math.Min(_home.AllApplications.Count, desired + (e.NewItems?.Count ?? 0));
        Synchronize(desired);
        Schedule();
    }

    private void Synchronize(int count)
    {
        if (_home is null) return;
        var desired = _home.AllApplications.Take(count).ToArray();
        var set = desired.ToHashSet();
        for (int i = _rendered.Count - 1; i >= 0; i--)
            if (!set.Contains(_rendered[i])) _rendered.RemoveAt(i);
        for (int i = 0; i < desired.Length; i++)
        {
            int existing = _rendered.IndexOf(desired[i]);
            if (existing < 0) _rendered.Insert(i, desired[i]);
            else if (existing != i) _rendered.Move(existing, i);
        }
    }

    private void Schedule()
    {
        if (!IsLoaded || _home is null || IsFullyRendered || _pending is not null) return;
        // Input, render and application-idle readiness run before the next batch.
        _pending = Dispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() =>
        {
            _pending = null;
            if (!IsLoaded || _home is null) return;
            Synchronize(Math.Min(_rendered.Count + 20, _home.AllApplications.Count));
            Schedule();
        }));
    }
}
