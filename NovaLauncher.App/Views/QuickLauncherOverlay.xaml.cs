using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NovaLauncher.App.ViewModels;

namespace NovaLauncher.App.Views;

public partial class QuickLauncherOverlay : UserControl
{
    private IInputElement? _previousFocus;
    private bool _textComposition;
    private bool _nativeComposition;
    private bool _commitKeyHeld;
    private HwndSource? _source;
    public bool IsComposing => _textComposition || _nativeComposition || _commitKeyHeld;
    private QuickLauncherViewModel? ViewModel => DataContext as QuickLauncherViewModel;
    public QuickLauncherOverlay()
    {
        InitializeComponent();
        TextCompositionManager.AddPreviewTextInputStartHandler(QueryBox, CompositionStarted);
        TextCompositionManager.AddPreviewTextInputUpdateHandler(QueryBox, CompositionUpdated);
        TextCompositionManager.AddPreviewTextInputHandler(QueryBox, CompositionCompleted);
        QueryBox.PreviewKeyUp += (_, _) => _commitKeyHeld = false;
        Loaded += (_, _) =>
        {
            _source = PresentationSource.FromVisual(this) as HwndSource;
            _source?.AddHook(ImeMessages);
        };
        Unloaded += (_, _) => { _source?.RemoveHook(ImeMessages); _source = null; };
    }
    private void CompositionStarted(object sender, TextCompositionEventArgs e) => _textComposition = true;
    private void CompositionUpdated(object sender, TextCompositionEventArgs e) => _textComposition = true;
    private void CompositionCompleted(object sender, TextCompositionEventArgs e)
    {
        if (_textComposition) _commitKeyHeld = Keyboard.IsKeyDown(Key.Enter) || Keyboard.IsKeyDown(Key.Space);
        _textComposition = false;
    }
    private IntPtr ImeMessages(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (QueryBox.IsKeyboardFocused && message == 0x010D) _nativeComposition = true; // WM_IME_STARTCOMPOSITION
        if (message == 0x010E) _nativeComposition = false; // WM_IME_ENDCOMPOSITION
        return IntPtr.Zero; // Never consume IME messages.
    }
    public void OpenAndFocus()
    {
        ViewModel?.Open();
        FocusQuery();
    }
    private void FocusQuery()
    {
        QueryBox.Focus();
        QueryBox.SelectAll();
    }
    private void Panel_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
        {
            _previousFocus = Keyboard.FocusedElement;
            PositionPanel();
            var duration = (Duration)FindResource("HoverDuration");
            PanelSurface.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
            Dispatcher.BeginInvoke(FocusQuery, DispatcherPriority.Input);
        }
        else
        {
            _textComposition = _nativeComposition = _commitKeyHeld = false;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ViewModel?.IsOpen != true && _previousFocus is UIElement { IsVisible: true, IsEnabled: true } element)
                    element.Focus();
            }), DispatcherPriority.Input);
        }
    }
    private void Overlay_SizeChanged(object sender, SizeChangedEventArgs e) => PositionPanel();
    private void PositionPanel()
    {
        double top = Math.Max(24, ActualHeight * 0.18);
        PanelSurface.Margin = new Thickness(16, top, 16, 16);
        PanelSurface.Width = Math.Min((double)FindResource("QuickLauncherWidth"), Math.Max(0, ActualWidth - 32));
        PanelSurface.MaxHeight = Math.Max(100, ActualHeight - top - 24);
    }
    private async void Overlay_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is not { IsOpen: true } vm || IsComposing || e.Key is Key.ImeProcessed or Key.DeadCharProcessed) return;
        // Preserve the close button's native Enter action when reached by Tab.
        if (e.Key == Key.Enter && Keyboard.FocusedElement is Button) return;
        switch (e.Key)
        {
            case Key.Down: vm.MoveSelection(1); e.Handled = true; break;
            case Key.Up: vm.MoveSelection(-1); e.Handled = true; break;
            case Key.Enter: e.Handled = true; await vm.LaunchSelectedAsync(); break;
            case Key.Escape: e.Handled = true; vm.Close(); break;
        }
    }
    private void Scrim_Click(object sender, MouseButtonEventArgs e) { ViewModel?.Close(); e.Handled = true; }
    private void Results_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultList.SelectedItem is { } selected) ResultList.ScrollIntoView(selected);
    }
    private async void Results_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source || ViewModel is not { } vm) return;
        var item = ItemsControl.ContainerFromElement(ResultList, source) as ListBoxItem;
        if (item?.DataContext is not ApplicationCardViewModel card) return;
        e.Handled = true;
        if (vm.LaunchCommand.CanExecute(card)) await vm.LaunchCommand.ExecuteAsync(card);
    }
}
