using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NovaLauncher.App.ViewModels;

namespace NovaLauncher.App.Views;

public partial class ApplicationCardView : UserControl
{
    private readonly DispatcherTimer _timeRefresh = new() { Interval = TimeSpan.FromSeconds(30) };

    public ApplicationCardView()
    {
        InitializeComponent();
        _timeRefresh.Tick += (_, _) => RefreshTime();
    }

    private void Card_MouseEnter(object sender, MouseEventArgs e) => UpdateInteraction();
    private void Card_MouseLeave(object sender, MouseEventArgs e) => UpdateInteraction();
    private void Card_FocusChanged(object sender, KeyboardFocusChangedEventArgs e) => UpdateInteraction();
    private void Card_Loaded(object sender, RoutedEventArgs e) => UpdateInteraction();
    private void Card_Unloaded(object sender, RoutedEventArgs e)
    {
        _timeRefresh.Stop();
        CategoryButton.IsChecked = false;
    }

    private void RefreshTime()
    {
        if (DataContext is ApplicationCardViewModel card) card.RefreshLaunchTime();
    }

    private void UpdateInteraction()
    {
        bool active = IsMouseOver || IsKeyboardFocusWithin || CategoryPopup.IsOpen;
        var duration = (Duration)FindResource("HoverDuration");
        double offset = active && duration.HasTimeSpan && duration.TimeSpan > TimeSpan.Zero
            ? -(double)FindResource("ApplicationCardLift") : 0;
        Lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offset, duration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
        HoverSurface.BeginAnimation(OpacityProperty, new DoubleAnimation(active ? 1 : 0, duration));
        NormalContent.BeginAnimation(OpacityProperty, new DoubleAnimation(active ? 0 : 1, duration));
        DetailsContent.BeginAnimation(OpacityProperty, new DoubleAnimation(active ? 1 : 0, duration));
        CategoryButton.BeginAnimation(OpacityProperty, new DoubleAnimation(active ? 1 : 0, duration));
        CategoryButton.IsHitTestVisible = active;
        if (active && IsLoaded)
        {
            RefreshTime();
            _timeRefresh.Start();
        }
        else _timeRefresh.Stop();
    }

    // Click feedback is view-only; Button.Command performs all launch and statistics work.
    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        var duration = SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast
            ? (Duration)FindResource("LaunchFeedbackDuration") : new Duration(TimeSpan.Zero);
        LaunchFlash.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, duration));
    }

    private void CategoryPopup_Opened(object? sender, EventArgs e)
    {
        CategoryChoices.GetBindingExpression(ListBox.SelectedItemProperty)?.UpdateTarget();
        UpdateInteraction();
        Dispatcher.BeginInvoke(new Action(() => CategoryChoices.Focus()));
    }
    private void CategoryPopup_Closed(object? sender, EventArgs e) => UpdateInteraction();
    private void CategoryPopup_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CategoryButton.IsChecked = false;
            CategoryButton.Focus();
            e.Handled = true;
        }
    }
    private async void CategoryChoices_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryPopup.IsOpen && CategoryChoices.IsKeyboardFocusWithin &&
            DataContext is ApplicationCardViewModel card && CategoryChoices.SelectedItem is CategoryChoice choice &&
            choice.Id != card.CategoryId && card.ChangeCategoryCommand.CanExecute(choice))
        {
            await card.ChangeCategoryCommand.ExecuteAsync(choice);
            if (card.CategoryId == choice.Id) CategoryButton.IsChecked = false;
        }
    }
}
