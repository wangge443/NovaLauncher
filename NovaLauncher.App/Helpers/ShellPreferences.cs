using System.Windows;
using System.Windows.Media;

namespace NovaLauncher.App.Helpers;

internal static class ShellPreferences
{
    private static readonly string[] BrushKeys =
    [
        "WindowBrush", "BackgroundFallbackBrush", "GlassTintBrush", "SidebarBrush", "BorderBrush",
        "TextBrush", "MutedTextBrush", "EmptyTextBrush", "EmptyIconBrush", "AccentBrush",
        "SelectedBrush", "HoverBrush", "CloseHoverBrush"
    ];

    public static void Apply(ResourceDictionary resources)
    {
        // A zero-duration transition preserves feedback when system animations are disabled.
        resources["HoverDuration"] = new Duration(TimeSpan.FromMilliseconds(
            SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast ? 160 : 0));

        foreach (string key in BrushKeys)
        {
            resources.Remove(key);
        }

        if (!SystemParameters.HighContrast)
        {
            return;
        }

        foreach (string key in BrushKeys)
        {
            resources[key] = key switch
            {
                "TextBrush" or "MutedTextBrush" or "EmptyTextBrush" or "EmptyIconBrush" or "BorderBrush" => SystemColors.WindowTextBrush,
                "AccentBrush" => SystemColors.HotTrackBrush,
                "HoverBrush" or "CloseHoverBrush" => new SolidColorBrush(SystemColors.WindowTextColor) { Opacity = 0.15 },
                "SelectedBrush" => new SolidColorBrush(SystemColors.WindowTextColor) { Opacity = 0.1 },
                _ => SystemColors.WindowBrush
            };
        }
    }
}
