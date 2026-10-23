using System.Windows.Controls;
using NovaLauncher.App.ViewModels;

namespace NovaLauncher.App.Views;

public partial class BackgroundStartupView : UserControl
{
    public BackgroundStartupView()
    {
        InitializeComponent();
        Loaded += (_, _) => (DataContext as BackgroundStartupViewModel)?.Refresh();
    }
}
