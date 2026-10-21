using System.Collections.ObjectModel;

namespace NovaLauncher.App.ViewModels;

public record NavigationItem(string Name, string Icon, Guid? CategoryId = null, WorkspaceMode Mode = WorkspaceMode.AllApplications)
{
    public bool IsSettings => Mode == WorkspaceMode.Settings;
}

public sealed class SidebarViewModel : ViewModelBase
{
    private readonly ContentViewModel _content;
    public UserProfileViewModel Profile { get; }
    public NavigationItem SettingsItem { get; } = new("设置", "\uE713", Mode: WorkspaceMode.Settings);
    public void OpenSettings() => SelectedItem = SettingsItem;
    public CategoryManagerViewModel CategoryManager => _content.CategoryManager;
    public ObservableCollection<CategoryViewModel> Categories => CategoryManager.Categories;
    public ObservableCollection<NavigationItem> Items { get; } = [new("全部应用", "\uE80A")];
    public IReadOnlyList<NavigationItem> FooterItems { get; } =
        [new("收藏", "\uE734", Mode: WorkspaceMode.Favorites), new("最近使用", "\uE81C", Mode: WorkspaceMode.Recent)];

    private NavigationItem _selectedItem;

    public SidebarViewModel(ContentViewModel content, UserProfileViewModel profile)
    {
        _content = content;
        Profile = profile;
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
            if (value is not null && (Items.Contains(value) || FooterItems.Contains(value) || value == SettingsItem))
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
