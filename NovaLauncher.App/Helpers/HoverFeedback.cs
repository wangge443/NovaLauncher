using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace NovaLauncher.App.Helpers;

// View-only behavior keeps animation timelines per control, avoiding frozen template resources.
public static class HoverFeedback
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(HoverFeedback), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not Border border)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            border.MouseEnter += OnMouseEnter;
            border.MouseLeave += OnMouseLeave;
        }
        else
        {
            border.MouseEnter -= OnMouseEnter;
            border.MouseLeave -= OnMouseLeave;
        }
    }

    private static void OnMouseEnter(object sender, MouseEventArgs e) => Animate((Border)sender, 1);
    private static void OnMouseLeave(object sender, MouseEventArgs e) => Animate((Border)sender, 0);

    private static void Animate(Border border, double opacity)
    {
        var duration = (Duration)border.FindResource("HoverDuration");
        border.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(opacity, duration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
    }
}
