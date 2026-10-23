using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NovaLauncher.App.Models;
using NovaLauncher.App.ViewModels;

namespace NovaLauncher.App.Views;

public partial class GlobalHotkeyView : UserControl
{
    public GlobalHotkeyView() => InitializeComponent();
    private void BeginCapture_Click(object sender, RoutedEventArgs e) => CaptureButton.Focus();
    private void View_Unloaded(object sender, RoutedEventArgs e) => (DataContext as GlobalHotkeyViewModel)?.CancelCapture();
    private async void Capture_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not GlobalHotkeyViewModel { IsCapturing: true } vm) return;
        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { vm.CancelCapture(); return; }
        if (e.IsRepeat || key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.ImeProcessed or Key.DeadCharProcessed) return;
        ModifierKeys modifiers = Keyboard.Modifiers;
        int flags = ((modifiers & ModifierKeys.Control) != 0 ? 2 : 0) | ((modifiers & ModifierKeys.Alt) != 0 ? 1 : 0)
            | ((modifiers & ModifierKeys.Shift) != 0 ? 4 : 0) | ((modifiers & ModifierKeys.Windows) != 0 ? 8 : 0);
        await vm.CaptureAsync(new GlobalHotkey(flags, KeyInterop.VirtualKeyFromKey(key)));
    }
}
