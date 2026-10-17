namespace NovaLauncher.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel(ContentViewModel content, UserProfileViewModel? profile = null, SettingsViewModel? settings = null)
    {
        Settings = settings;
        Content = content;
        Sidebar = new(content, profile ?? new UserProfileViewModel(null, new Services.AvatarFilePicker(), content.Toast));
        Sidebar.Profile.SettingsRequested += Sidebar.OpenSettings;
        content.Home.NavigationRequested += mode => Sidebar.SelectedItem = Sidebar.FooterItems.Single(item => item.Mode == mode);
    }
    public ContentViewModel Content { get; }
    public SettingsViewModel? Settings { get; }
    public void ApplyStartupPage(Models.StartupPage page)
    {
        Sidebar.SelectedItem = page switch
        {
            Models.StartupPage.Recent => Sidebar.FooterItems.Single(x => x.Mode == WorkspaceMode.Recent),
            Models.StartupPage.Favorites => Sidebar.FooterItems.Single(x => x.Mode == WorkspaceMode.Favorites),
            _ => Sidebar.Items[0]
        };
    }
    public string Title => "Nova Launcher";

    public SidebarViewModel Sidebar { get; }
}
