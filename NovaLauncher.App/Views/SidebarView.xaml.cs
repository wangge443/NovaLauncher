using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using NovaLauncher.App.ViewModels;

namespace NovaLauncher.App.Views;

public partial class SidebarView : UserControl
{
    public SidebarView() => InitializeComponent();
    private void BeginCategory_Click(object sender, RoutedEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() => CategoryNameEditor.Focus()));

    private void CategoryEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not SidebarViewModel sidebar) return;
        if (e.Key == Key.Escape)
        {
            sidebar.CategoryManager.CancelCreateCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (sidebar.CategoryManager.CreateCommand.CanExecute(null)) sidebar.CategoryManager.CreateCommand.Execute(null);
            e.Handled = true;
        }
    }
}
