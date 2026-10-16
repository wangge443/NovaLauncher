using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace NovaLauncher.App.Views;

public partial class ToastView : UserControl
{
    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(ToastView), new PropertyMetadata(false, OnIsOpenChanged));
    public bool IsOpen { get => (bool)GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }

    public ToastView() => InitializeComponent();

    private static void OnIsOpenChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var view = (ToastView)sender;
        bool open = (bool)e.NewValue;
        view.Visibility = Visibility.Visible;
        var duration = (Duration)view.FindResource("HoverDuration");
        var animation = new DoubleAnimation(open ? 1 : 0, duration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        animation.Completed += (_, _) =>
        {
            if (!view.IsOpen) view.Visibility = Visibility.Hidden;
        };
        view.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }
}
