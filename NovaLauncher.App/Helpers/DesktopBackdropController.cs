using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;

namespace NovaLauncher.App.Helpers;

// Experimental Windows System Backdrop. API success does not guarantee visible wallpaper.
// Owns only the HWND material. Layout, navigation and business state stay outside this class.
internal sealed class DesktopBackdropController : IDisposable
{
    private const int SystemBackdropType = 38;
    private const int UseImmersiveDarkMode = 20;
    private const int CornerPreference = 33;
    private readonly Window _window;
    private readonly Action<bool, string> _report;
    private HwndSource? _source;
    private bool _disposed;
    private bool _refreshQueued;

    public bool Enabled { get; set; } = true;

    public DesktopBackdropController(Window window, Action<bool, string> report)
    {
        _window = window;
        _report = report;
        _window.SourceInitialized += OnWindowChanged;
        _window.Activated += OnWindowChanged;
        _window.Deactivated += OnWindowChanged;
        _window.StateChanged += OnWindowChanged;
        SystemParameters.StaticPropertyChanged += OnSystemChanged;
        AttachSource();
        Refresh();
    }

    private void AttachSource()
    {
        if (_source is not null) return;
        var handle = new WindowInteropHelper(_window).Handle;
        if (handle == IntPtr.Zero) return;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WindowProcedure);
    }

    private void OnWindowChanged(object? sender, EventArgs e) => QueueRefresh();
    private void OnSystemChanged(object? sender, PropertyChangedEventArgs e) => QueueRefresh();

    private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Settings, theme, DWM composition, power and display changes.
        if (message is 0x001A or 0x031A or 0x031E or 0x0218 or 0x007E)
            QueueRefresh();
        return IntPtr.Zero;
    }

    private void QueueRefresh()
    {
        if (_disposed || _refreshQueued || _window.Dispatcher.HasShutdownStarted) return;
        _refreshQueued = true;
        _window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _refreshQueued = false;
            if (!_disposed) Refresh();
        }));
    }

    public void Refresh()
    {
        if (_disposed) return;
        AttachSource();
        string? reason = GetFallbackReason();
        if (reason is not null || _source?.CompositionTarget is null)
        {
            UseFallback(reason ?? "WindowNotReady");
            return;
        }

        try
        {
            if (NativeMethods.DwmIsCompositionEnabled(out bool composition) < 0 || !composition)
            {
                UseFallback("CompositionUnavailable");
                return;
            }

            // Desktop Acrylic, not Mica: blur the actual content behind this window.
            int light = 0, acrylic = 3, round = 2;
            int result = NativeMethods.DwmSetWindowAttribute(_source.Handle, UseImmersiveDarkMode, ref light, sizeof(int));
            if (result < 0)
            {
                UseFallback($"DwmError:0x{result:X8}");
                return;
            }

            _window.SetCurrentValue(Window.BackgroundProperty, Brushes.Transparent);
            _source.CompositionTarget.BackgroundColor = Color.FromArgb(0, 0, 0, 0);
            // WindowChrome must retain full-client glass across resize/maximize operations.
            var chrome = WindowChrome.GetWindowChrome(_window);
            chrome?.SetCurrentValue(WindowChrome.GlassFrameThicknessProperty, new Thickness(-1));
            var margins = new NativeMethods.Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            result = NativeMethods.DwmExtendFrameIntoClientArea(_source.Handle, ref margins);
            if (result < 0)
            {
                UseFallback($"FrameError:0x{result:X8}");
                return;
            }

            NativeMethods.DwmSetWindowAttribute(_source.Handle, CornerPreference, ref round, sizeof(int));
            // WindowChrome changes the frame/region; apply the backdrop after those updates.
            result = NativeMethods.DwmSetWindowAttribute(_source.Handle, SystemBackdropType, ref acrylic, sizeof(int));
            if (result < 0)
            {
                UseFallback($"DwmError:0x{result:X8}");
                return;
            }
            _report(true, "DesktopAcrylic");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or COMException)
        {
            UseFallback(ex.GetType().Name);
        }
    }

    private string? GetFallbackReason()
    {
        if (!Enabled) return "NonDesktopMode";
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)) return "UnsupportedWindows";
        if (SystemParameters.HighContrast) return "HighContrast";
        if (!SystemParameters.UIEffects) return "VisualEffectsDisabled";
        if (SystemParameters.IsRemoteSession) return "RemoteSession";
        if (!_window.IsActive) return "InactiveWindow";
        // Diagnostic opt-out allows validating fallback without modifying the user's settings.
        if (Environment.GetEnvironmentVariable("NOVA_DISABLE_SYSTEM_BACKDROP") == "1") return "DiagnosticOverride";
        try
        {
            using var preferences = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            // Conservative check; DWM remains authoritative for OS/hardware policy.
            if (preferences?.GetValue("EnableTransparency") is not int enabled) return "TransparencySettingUnavailable";
            if (enabled == 0) return "TransparencyDisabled";
            if (!NativeMethods.GetSystemPowerStatus(out var power)) return "PowerStatusUnavailable";
            if (power.SystemStatusFlag == 1) return "EnergySaver";
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return "SettingsUnavailable";
        }
        return null;
    }

    private void UseFallback(string reason)
    {
        // Paint opaque first: even a failing native reset must never leave an unreadable window.
        _report(false, reason);
        _window.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        if (_source?.CompositionTarget is not null)
            _source.CompositionTarget.BackgroundColor =
                (_window.TryFindResource("BackgroundFallbackBrush") as SolidColorBrush)?.Color ?? SystemColors.WindowColor;
        WindowChrome.GetWindowChrome(_window)?.SetCurrentValue(WindowChrome.GlassFrameThicknessProperty, new Thickness(0));
        if (_source is null || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)) return;
        try
        {
            int none = 1;
            NativeMethods.DwmSetWindowAttribute(_source.Handle, SystemBackdropType, ref none, sizeof(int));
            var margins = new NativeMethods.Margins();
            NativeMethods.DwmExtendFrameIntoClientArea(_source.Handle, ref margins);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or COMException)
        {
            Trace.WriteLine($"Backdrop reset unavailable: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _window.SourceInitialized -= OnWindowChanged;
        _window.Activated -= OnWindowChanged;
        _window.Deactivated -= OnWindowChanged;
        _window.StateChanged -= OnWindowChanged;
        SystemParameters.StaticPropertyChanged -= OnSystemChanged;
        _source?.RemoveHook(WindowProcedure);
        if (_source?.IsDisposed == false) UseFallback("Detached");
        _source = null;
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Margins { public int Left, Right, Top, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PowerStatus
        {
            public byte AcLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
            public uint BatteryLifeTime, BatteryFullLifeTime;
        }

        [DllImport("dwmapi.dll")]
        internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("dwmapi.dll")]
        internal static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
        [DllImport("dwmapi.dll")]
        internal static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSystemPowerStatus(out PowerStatus status);
    }
}
