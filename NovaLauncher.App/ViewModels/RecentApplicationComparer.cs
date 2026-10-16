using System.Collections;

namespace NovaLauncher.App.ViewModels;

internal sealed class RecentApplicationComparer : IComparer, IComparer<ApplicationCardViewModel>
{
    public static readonly RecentApplicationComparer Instance = new();
    public int Compare(object? x, object? y) => Compare((ApplicationCardViewModel?)x, (ApplicationCardViewModel?)y);
    public int Compare(ApplicationCardViewModel? left, ApplicationCardViewModel? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left is null) return 1;
        if (right is null) return -1;
        int time = Nullable.Compare(right.LastLaunchTime, left.LastLaunchTime);
        if (time != 0) return time;
        int name = StringComparer.OrdinalIgnoreCase.Compare(left.DisplayName, right.DisplayName);
        return name != 0 ? name : left.Application.Id.CompareTo(right.Application.Id);
    }
}
