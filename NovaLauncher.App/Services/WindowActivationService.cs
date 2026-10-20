using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace NovaLauncher.App.Services;

public static class WindowActivationService
{
    public static void Activate(Window window)
    {
        if (window.WindowState == WindowState.Minimized) SystemCommands.RestoreWindow(window);
        if (!window.IsVisible) window.Show();
        window.Activate();
        SetForegroundWindow(new WindowInteropHelper(window).Handle);
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
}
