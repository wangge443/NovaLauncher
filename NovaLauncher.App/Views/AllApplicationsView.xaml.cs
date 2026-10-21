using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NovaLauncher.App.ViewModels;

namespace NovaLauncher.App.Views;

public partial class AllApplicationsView : UserControl
{
    private readonly ObservableCollection<ApplicationCardViewModel> _rendered = [];
    private ContentViewModel? _content;
    private DispatcherOperation? _pending;
    public bool IsFullyRendered => _content is null || _rendered.Count == _content.Applications.Count;

    public AllApplicationsView()
    {
        InitializeComponent();
        AllApplications.ItemsSource = _rendered;
        DataContextChanged += (_, _) => Attach();
        Loaded += (_, _) => { Attach(); Schedule(); };
        Unloaded += (_, _) => Detach();
    }

    private void Attach()
    {
        if (ReferenceEquals(_content, DataContext)) return;
        Detach();
        _content = DataContext as ContentViewModel;
        _rendered.Clear();
        if (_content is null) return;
        ((INotifyCollectionChanged)_content.Applications).CollectionChanged += SourceChanged;
        int initial = _content.Applications.Count <= 100 ? _content.Applications.Count : 40;
        foreach (var card in _content.Applications.Take(initial)) _rendered.Add(card);
        Schedule();
    }

    private void Detach()
    {
        _pending?.Abort();
        _pending = null;
        if (_content is not null)
            ((INotifyCollectionChanged)_content.Applications).CollectionChanged -= SourceChanged;
        _content = null;
    }

    private void SourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_content is null) return;
        // Keep existing containers, order and object identity. A change never rebuilds ViewModels.
        int desired = Math.Min(_content.Applications.Count, Math.Max(_rendered.Count,
            _content.Applications.Count <= 100 ? _content.Applications.Count : 40));
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewStartingIndex <= _rendered.Count)
            desired = Math.Min(_content.Applications.Count, desired + (e.NewItems?.Count ?? 0));
        Synchronize(desired);
        Schedule();
    }

    private void Synchronize(int count)
    {
        if (_content is null) return;
        var desired = _content.Applications.Take(count).ToArray();
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
        if (!IsLoaded || _content is null || IsFullyRendered || _pending is not null) return;
        // Input, render and application-idle readiness run before the next batch.
        _pending = Dispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() =>
        {
            _pending = null;
            if (!IsLoaded || _content is null) return;
            Synchronize(Math.Min(_rendered.Count + 20, _content.Applications.Count));
            Schedule();
        }));
    }
}
