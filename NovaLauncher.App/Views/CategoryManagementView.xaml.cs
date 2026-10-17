using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NovaLauncher.App.ViewModels;

namespace NovaLauncher.App.Views;

public partial class CategoryManagementView : UserControl
{
    public CategoryManagementView() => InitializeComponent();
    private void BeginCategory_Click(object sender, RoutedEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() => { CategoryNameEditor.BringIntoView(); CategoryNameEditor.Focus(); }));
    private void CancelCategory_Click(object sender, RoutedEventArgs e) => NewCategoryButton.Focus();
    private void CategoryEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not CategoryManagerViewModel manager) return;
        if (e.Key == Key.Escape)
        {
            manager.CancelCreateCommand.Execute(null);
            NewCategoryButton.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (manager.CreateCommand.CanExecute(null)) manager.CreateCommand.Execute(null);
            e.Handled = true;
        }
    }
}
