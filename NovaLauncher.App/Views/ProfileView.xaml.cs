using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NovaLauncher.App.ViewModels;

namespace NovaLauncher.App.Views;

public partial class ProfileView : UserControl
{
    private Window? _window;
    private Point _anchor;
    private UserProfileViewModel? Profile => DataContext as UserProfileViewModel;
    public ProfileView() => InitializeComponent();
    private void Profile_Loaded(object sender, RoutedEventArgs e)
    {
        _window = Window.GetWindow(this);
        if (_window is not null) { _window.LocationChanged += Reposition; _window.SizeChanged += Reposition; _window.StateChanged += Reposition; _window.Deactivated += Window_Deactivated; _window.PreviewMouseDown += Outside_MouseDown; }
        LayoutUpdated += LayoutChanged;
    }
    private void Profile_Unloaded(object sender, RoutedEventArgs e)
    {
        if (Profile is { } vm) vm.IsProfileFlyoutOpen = false;
        if (_window is not null) { _window.LocationChanged -= Reposition; _window.SizeChanged -= Reposition; _window.StateChanged -= Reposition; _window.Deactivated -= Window_Deactivated; _window.PreviewMouseDown -= Outside_MouseDown; }
        LayoutUpdated -= LayoutChanged; _window = null;
    }
    private void Window_Deactivated(object? sender, EventArgs e) { if (Profile is { } vm) vm.IsProfileFlyoutOpen = false; }
    private void Avatar_Click(object sender, RoutedEventArgs e)
    {
        Profile?.ToggleProfileFlyoutCommand.Execute(null);
    }
    private void Flyout_Opened(object? sender, EventArgs e)
    {
        _anchor = AvatarButton.PointToScreen(new Point());
        Dispatcher.BeginInvoke(new Action(() => { if (Flyout.IsOpen) EditNameButton.Focus(); }));
    }
    private void Outside_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Explicit outside dismissal avoids Popup capture reopening the avatar on the same click.
        if (Flyout.IsOpen && !AvatarButton.IsMouseOver && !Flyout.Child.IsMouseOver)
            Profile?.CloseProfileFlyoutCommand.Execute(null);
    }
    private void EditName_Click(object sender, RoutedEventArgs e) => Dispatcher.BeginInvoke(new Action(() => { NameEditor.Focus(); NameEditor.SelectAll(); }));
    private async void Flyout_KeyDown(object sender, KeyEventArgs e)
    {
        if (Profile is not { } vm) return;
        if (e.Key == Key.Escape)
        {
            if (vm.IsEditingName) { vm.CancelEditNameCommand.Execute(null); EditNameButton.Focus(); }
            else { vm.CloseProfileFlyoutCommand.Execute(null); AvatarButton.Focus(); }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && vm.IsEditingName && NameEditor.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            if (vm.SaveNameCommand.CanExecute(null)) await vm.SaveNameCommand.ExecuteAsync(null);
            if (!vm.IsEditingName && Flyout.IsOpen) EditNameButton.Focus();
        }
    }
    private void LayoutChanged(object? sender, EventArgs e)
    {
        if (Flyout.IsOpen && IsLoaded)
        {
            var point = AvatarButton.PointToScreen(new Point());
            if (point != _anchor) { _anchor = point; Reposition(sender, e); }
        }
    }
    private void Reposition(object? sender, EventArgs e)
    {
        if (!Flyout.IsOpen) return;
        double offset = Flyout.HorizontalOffset;
        Flyout.SetCurrentValue(System.Windows.Controls.Primitives.Popup.HorizontalOffsetProperty, offset + 0.1);
        Flyout.SetCurrentValue(System.Windows.Controls.Primitives.Popup.HorizontalOffsetProperty, offset);
    }
}
