namespace NovaLauncher.App.ViewModels;

public sealed class MainWindowViewModel(ContentViewModel content) : ViewModelBase
{
    public ContentViewModel Content { get; } = content;
    public string Title => "Nova Launcher";

    public SidebarViewModel Sidebar { get; } = new();
}
