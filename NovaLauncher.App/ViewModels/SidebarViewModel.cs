namespace NovaLauncher.App.ViewModels;

public sealed record NavigationItem(string Name, string Icon);

public sealed class SidebarViewModel : ViewModelBase
{
    public IReadOnlyList<NavigationItem> Items { get; } =
    [
        new("首页", "\uE80F"),
        new("游戏", "\uE7FC"),
        new("应用", "\uE71D"),
        new("收藏", "\uE734"),
        new("最近使用", "\uE81C"),
        new("设置", "\uE713")
    ];

    private NavigationItem _selectedItem;

    public SidebarViewModel()
    {
        _selectedItem = Items[0];
    }

    public NavigationItem SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (value is not null && Items.Contains(value))
            {
                SetProperty(ref _selectedItem, value);
            }
        }
    }
}
