namespace NovaLauncher.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel(ContentViewModel content, UserProfileViewModel? profile = null)
    {
        Content = content;
        Sidebar = new(content, profile ?? new UserProfileViewModel(null, new Services.AvatarFilePicker(), content.Toast));
        Sidebar.Profile.SettingsRequested += Sidebar.OpenSettings;
        content.Home.NavigationRequested += mode => Sidebar.SelectedItem = Sidebar.FooterItems.Single(item => item.Mode == mode);
    }
    public ContentViewModel Content { get; }
    public string Title => "Nova Launcher";

    public SidebarViewModel Sidebar { get; }
}
