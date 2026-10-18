using System.Collections;
using NovaLauncher.App.ViewModels;

namespace NovaLauncher.App.Services;

// A projection over existing cards; no I/O and no new card instances.
public sealed class ApplicationSearchService : IComparer
{
    public string Query { get; set; } = "";
    public bool Matches(ApplicationCardViewModel card) => card.DisplayName.Contains(Query, StringComparison.OrdinalIgnoreCase);
    private int Rank(string name) => name.Equals(Query, StringComparison.OrdinalIgnoreCase) ? 0
        : name.StartsWith(Query, StringComparison.OrdinalIgnoreCase) ? 1 : 2;
    public int Compare(object? x, object? y)
    {
        var left = (ApplicationCardViewModel)x!;
        var right = (ApplicationCardViewModel)y!;
        int rank = Rank(left.DisplayName).CompareTo(Rank(right.DisplayName));
        return rank != 0 ? rank : RecentApplicationComparer.Instance.Compare(left, right);
    }
}
