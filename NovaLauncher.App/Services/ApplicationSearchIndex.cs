using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using NovaLauncher.App.ViewModels;

namespace NovaLauncher.App.Services;

// Owned by one search session owner, on the same UI thread as Applications.
public sealed class ApplicationSearchIndex : IDisposable
{
    private readonly ObservableCollection<ApplicationCardViewModel> _source;
    private readonly Dictionary<ApplicationCardViewModel, ApplicationSearchEntry> _entries = [];

    public ApplicationSearchIndex(ObservableCollection<ApplicationCardViewModel> source)
    {
        _source = source;
        foreach (var card in source) Add(card);
        source.CollectionChanged += SourceChanged;
    }

    public int Count => _entries.Count;
    public ApplicationSearchEntry? GetEntry(ApplicationCardViewModel card) => _entries.GetValueOrDefault(card);

    private void Add(ApplicationCardViewModel card)
    {
        if (_entries.ContainsKey(card)) return;
        _entries.Add(card, ApplicationSearchEntry.Create(card.Application.Id, card.DisplayName));
        card.PropertyChanged += CardChanged;
    }

    private void Remove(ApplicationCardViewModel card)
    {
        card.PropertyChanged -= CardChanged;
        _entries.Remove(card);
    }

    private void SourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Move) return;
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var card in _entries.Keys.ToArray()) Remove(card);
            foreach (var card in _source) Add(card);
            return;
        }
        if (e.OldItems is not null)
            foreach (ApplicationCardViewModel card in e.OldItems) Remove(card);
        if (e.NewItems is not null)
            foreach (ApplicationCardViewModel card in e.NewItems) Add(card);
    }

    private void CardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is ApplicationCardViewModel card &&
            (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(ApplicationCardViewModel.DisplayName)) &&
            _entries.TryGetValue(card, out var previous) && previous.OriginalName != card.DisplayName)
            _entries[card] = ApplicationSearchEntry.Create(card.Application.Id, card.DisplayName);
    }

    public void Dispose()
    {
        _source.CollectionChanged -= SourceChanged;
        foreach (var card in _entries.Keys) card.PropertyChanged -= CardChanged;
        _entries.Clear();
    }
}
