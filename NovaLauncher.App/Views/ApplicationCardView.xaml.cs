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
        long phase = Diagnostics.PerformanceTrace.Start();
        InitializeComponent();
        Diagnostics.PerformanceTrace.End("CardXamlConstruction", phase);
        _timeRefresh.Tick += (_, _) => RefreshTime();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                _timeRefresh.Stop();
                CardMenu.IsOpen = InfoPopup.IsOpen = RenamePopup.IsOpen = RemovePopup.IsOpen = false;
            }
            else UpdateInteraction();
        };
    }

    private void Card_MouseEnter(object sender, MouseEventArgs e) => UpdateInteraction();
    private void Card_MouseLeave(object sender, MouseEventArgs e) => UpdateInteraction();
    private void Card_FocusChanged(object sender, KeyboardFocusChangedEventArgs e) => UpdateInteraction();
    private void Card_Loaded(object sender, RoutedEventArgs e) => UpdateInteraction();
    private void Card_Unloaded(object sender, RoutedEventArgs e)
    {
        _timeRefresh.Stop();
        CardMenu.IsOpen = false;
        InfoPopup.IsOpen = false;
        RenamePopup.IsOpen = false;
        RemovePopup.IsOpen = false;
    }

    private void RefreshTime()
    {
        if (DataContext is ApplicationCardViewModel card) card.RefreshLaunchTime();
    }

    private void UpdateInteraction()
    {
        bool active = IsVisible && (IsMouseOver || IsKeyboardFocusWithin || CardMenu.IsOpen || InfoPopup.IsOpen || RenamePopup.IsOpen || RemovePopup.IsOpen);
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

    private void Card_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // WPF handles the Apps key on key-up; do not open twice for the same gesture.
        if (e.Key == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift)
        {
            CardMenu.PlacementTarget = this;
            CardMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            CardMenu.IsOpen = true;
            e.Handled = true;
        }
    }
    private void CardMenu_Opened(object sender, RoutedEventArgs e) => UpdateInteraction();
    private void CardMenu_Closed(object sender, RoutedEventArgs e)
    {
        CardMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        UpdateInteraction();
    }
    private void InfoMenu_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        Dispatcher.BeginInvoke(new Action(() => { if (IsLoaded) InfoPopup.IsOpen = true; }));
    }
    private void InfoPopup_Opened(object? sender, EventArgs e)
    {
        RefreshTime();
        UpdateInteraction();
        Dispatcher.BeginInvoke(new Action(() => CloseInfoButton.Focus()));
    }
    private void InfoPopup_Closed(object? sender, EventArgs e)
    {
        if (IsLoaded) LaunchButton.Focus();
        UpdateInteraction();
    }
    private void CloseInfo_Click(object sender, RoutedEventArgs e) => InfoPopup.IsOpen = false;
    private void RenameMenu_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not ApplicationCardViewModel card || !card.BeginRenameCommand.CanExecute(null)) return;
        card.BeginRenameCommand.Execute(null);
        Dispatcher.BeginInvoke(new Action(() => { if (IsLoaded) RenamePopup.IsOpen = true; }));
    }
    private void RenamePopup_Opened(object? sender, EventArgs e)
    {
        UpdateInteraction();
        Dispatcher.BeginInvoke(new Action(() => { RenameEditor.Focus(); RenameEditor.SelectAll(); }));
    }
    private void EditorPopup_Closed(object? sender, EventArgs e)
    {
        if (IsLoaded) LaunchButton.Focus();
        UpdateInteraction();
    }
    private void CancelRename_Click(object sender, RoutedEventArgs e) => RenamePopup.IsOpen = false;
    private async void SaveRename_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not ApplicationCardViewModel card || !card.RenameCommand.CanExecute(null)) return;
        await card.RenameCommand.ExecuteAsync(null);
        if (card.RenameError is null) RenamePopup.IsOpen = false;
    }
    private void Rename_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { RenamePopup.IsOpen = false; e.Handled = true; }
        else if (e.Key == Key.Enter && RenameEditor.IsKeyboardFocused) SaveRename_Click(sender, e);
    }
    private void RemoveMenu_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        Dispatcher.BeginInvoke(new Action(() => { if (IsLoaded) RemovePopup.IsOpen = true; }));
    }
    private void RemovePopup_Opened(object? sender, EventArgs e)
    {
        UpdateInteraction();
        Dispatcher.BeginInvoke(new Action(() => CancelRemoveButton.Focus()));
    }
    private void CancelRemove_Click(object sender, RoutedEventArgs e) => RemovePopup.IsOpen = false;
    private void Remove_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { RemovePopup.IsOpen = false; e.Handled = true; }
    }
    private void Info_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        InfoPopup.IsOpen = false;
        e.Handled = true;
    }
}
