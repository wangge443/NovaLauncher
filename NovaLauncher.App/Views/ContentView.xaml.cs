using System.Windows.Controls;
using System.Windows;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NovaLauncher.App.ViewModels;

namespace NovaLauncher.App.Views;

public partial class ContentView : UserControl
{
    public ContentView() => InitializeComponent();

    private static string[] GetPaths(DragEventArgs e)
    {
        try
        {
            return e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] paths ? paths : [];
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException or InvalidOperationException)
        {
            Trace.TraceWarning("File drop data unavailable: {0}", ex.GetType().Name);
            return [];
        }
    }

    private void Workspace_DragOver(object sender, DragEventArgs e)
    {
        if (DataContext is ContentViewModel viewModel)
            e.Effects = viewModel.PreviewDrop(GetPaths(e)) && e.AllowedEffects.HasFlag(DragDropEffects.Copy)
                ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Workspace_DragLeave(object sender, DragEventArgs e)
    {
        // Also reset when Escape cancels the OLE drag while the pointer stays inside.
        if (DataContext is ContentViewModel viewModel)
            viewModel.EndDrag();
        e.Handled = true;
    }

    private async void Workspace_Drop(object sender, DragEventArgs e)
    {
        string[] paths = GetPaths(e);
        e.Handled = true;
        if (DataContext is ContentViewModel viewModel)
            await viewModel.ImportAsync(paths);
    }
}
