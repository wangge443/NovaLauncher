using System.Collections.ObjectModel;

namespace NovaLauncher.App.ViewModels;

public record NavigationItem(string Name, string Icon, Guid? CategoryId = null, WorkspaceMode Mode = WorkspaceMode.Home)
{
    public bool IsSettings => CategoryId is null && Name == "设置";
}

public sealed class SidebarViewModel : ViewModelBase
{
    private readonly ContentViewModel _content;
    public CategoryManagerViewModel CategoryManager => _content.CategoryManager;
    public ObservableCollection<CategoryViewModel> Categories => CategoryManager.Categories;
    public ObservableCollection<NavigationItem> Items { get; } = [new("首页", "\uE80F")];
    public IReadOnlyList<NavigationItem> FooterItems { get; } =
        [new("收藏", "\uE734", Mode: WorkspaceMode.Favorites), new("最近使用", "\uE81C"), new("设置", "\uE713")];

    private NavigationItem _selectedItem;

    public SidebarViewModel(ContentViewModel content)
    {
        _content = content;
        _selectedItem = Items[0];
        foreach (var category in Categories) Items.Add(category);
        Categories.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
                foreach (CategoryViewModel category in e.NewItems) Items.Add(category);
        };
    }

    public NavigationItem SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (value is not null && (Items.Contains(value) || FooterItems.Contains(value)))
            {
                if (SetProperty(ref _selectedItem, value))
                {
                    _content.SelectWorkspace(value is CategoryViewModel ? WorkspaceMode.Category : value.Mode, value as CategoryViewModel);
                    OnPropertyChanged(nameof(TopSelectedItem));
                    OnPropertyChanged(nameof(FooterSelectedItem));
                }
            }
        }
    }
    public NavigationItem? TopSelectedItem
    {
        get => Items.Contains(SelectedItem) ? SelectedItem : null;
        set { if (value is not null) SelectedItem = value; }
    }
    public NavigationItem? FooterSelectedItem
    {
        get => FooterItems.Contains(SelectedItem) ? SelectedItem : null;
        set { if (value is not null) SelectedItem = value; }
    }
}
