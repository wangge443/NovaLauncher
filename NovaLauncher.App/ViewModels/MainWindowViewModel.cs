namespace NovaLauncher.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    public string Title => "Nova Launcher";

    public SidebarViewModel Sidebar { get; } = new();
}
