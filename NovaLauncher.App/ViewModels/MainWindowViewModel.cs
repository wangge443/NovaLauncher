namespace NovaLauncher.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel(ContentViewModel content)
    {
        Content = content;
        Sidebar = new(content);
        content.Home.NavigationRequested += mode => Sidebar.SelectedItem = Sidebar.FooterItems.Single(item => item.Mode == mode);
    }
    public ContentViewModel Content { get; }
    public string Title => "Nova Launcher";

    public SidebarViewModel Sidebar { get; }
}
