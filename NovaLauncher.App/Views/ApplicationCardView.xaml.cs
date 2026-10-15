using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NovaLauncher.App.Views;

public partial class ApplicationCardView : UserControl
{
    public ApplicationCardView() => InitializeComponent();

    private void Card_MouseEnter(object sender, MouseEventArgs e) => AnimateLift(true);
    private void Card_MouseLeave(object sender, MouseEventArgs e) => AnimateLift(false);

    private void AnimateLift(bool hovered)
    {
        var duration = (Duration)FindResource("HoverDuration");
        double offset = hovered && duration.HasTimeSpan && duration.TimeSpan > TimeSpan.Zero
            ? -(double)FindResource("ApplicationCardLift") : 0;
        Lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offset, duration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
    }
}
