using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NovaLauncher.App.Views;

namespace NovaLauncher.App.Helpers;

internal static class TransparencyWindowHost
{
    public static MainWindow CreateReplacement(MainWindow previous, bool transparent)
    {
        Rect bounds = previous.WindowState == WindowState.Normal
            ? new Rect(previous.Left, previous.Top, previous.ActualWidth, previous.ActualHeight) : previous.RestoreBounds;
        var next = new MainWindow
        {
            DataContext = previous.DataContext, WindowStartupLocation = WindowStartupLocation.Manual,
            Left = bounds.Left, Top = bounds.Top, Width = bounds.Width, Height = bounds.Height,
            Topmost = previous.Topmost
        };
        next.ConfigureTransparentSurface(transparent);
        var sidebarWidth = ((ColumnDefinition)previous.FindName("SidebarColumn")).Width;
        ((ColumnDefinition)next.FindName("SidebarColumn")).Width = sidebarWidth;
        var settings = Find<SettingsView>(previous);
        bool expanded = settings is not null && ((Expander)settings.FindName("BackgroundExpander")).IsExpanded;
        double offset = settings is null ? 0 : Find<ScrollViewer>(settings)?.VerticalOffset ?? 0;
        next.Loaded += (_, _) =>
        {
            if (Find<SettingsView>(next) is { } restored)
            {
                ((Expander)restored.FindName("BackgroundExpander")).IsExpanded = expanded;
                Find<ScrollViewer>(restored)?.ScrollToVerticalOffset(offset);
            }
        };
        return next;
    }

    private static T? Find<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            if (Find<T>(child) is { } nested) return nested;
        }
        return null;
    }
}
