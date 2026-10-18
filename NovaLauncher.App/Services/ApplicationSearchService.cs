using System.Collections;
using System.Collections.ObjectModel;
using NovaLauncher.App.ViewModels;

namespace NovaLauncher.App.Services;

// A projection over existing cards; no I/O and no new card instances.
public sealed class ApplicationSearchService : IComparer, IDisposable
{
    private string _query = "";
    private string _normalizedQuery = "";

    public ApplicationSearchService(ObservableCollection<ApplicationCardViewModel> source) => Index = new(source);
    public ApplicationSearchIndex Index { get; }
    public string Query
    {
        get => _query;
        set
        {
            _query = value.Trim();
            _normalizedQuery = ApplicationSearchEntry.Normalize(_query);
        }
    }

    public bool Matches(ApplicationCardViewModel card) => Rank(card) != int.MaxValue;

    public int Rank(ApplicationCardViewModel card)
    {
        var entry = Index.GetEntry(card);
        if (entry is null) return int.MaxValue;
        int direct = Match(entry.OriginalName, _query);
        if (direct < 3) return direct;
        if (_normalizedQuery.Length == 0) return int.MaxValue;
        int normalized = Match(entry.NormalizedName, _normalizedQuery);
        if (normalized < 3) return 3 + normalized;
        int full = Match(entry.FullPinyin, _normalizedQuery);
        if (full < 2) return 6 + full;
        int initials = Match(entry.PinyinInitials, _normalizedQuery);
        if (initials < 2) return 8 + initials;
        return full == 2 ? 10 : initials == 2 ? 11 : int.MaxValue;
    }

    private static int Match(string key, string query) => key.Equals(query, StringComparison.OrdinalIgnoreCase) ? 0
        : key.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 1
        : key.Contains(query, StringComparison.OrdinalIgnoreCase) ? 2 : 3;
    public int Compare(object? x, object? y)
    {
        var left = (ApplicationCardViewModel)x!;
        var right = (ApplicationCardViewModel)y!;
        int rank = Rank(left).CompareTo(Rank(right));
        return rank != 0 ? rank : RecentApplicationComparer.Instance.Compare(left, right);
    }

    public void Dispose() => Index.Dispose();
}
