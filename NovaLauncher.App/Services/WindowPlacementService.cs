using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using NovaLauncher.App.Models;

namespace NovaLauncher.App.Services;

// All geometry is in WPF DIPs. Win32 work areas are converted using this HWND's coordinate scale.
public static class WindowPlacementService
{
    public static Rect Resolve(AppSettings settings, IReadOnlyList<Rect> workAreas, Rect primary, double defaultWidth = 1120, double defaultHeight = 720)
    {
        settings = AppSettingsService.Validate(settings);
        double width = settings.RememberWindowSize ? settings.WindowWidth ?? defaultWidth : defaultWidth;
        double height = settings.RememberWindowSize ? settings.WindowHeight ?? defaultHeight : defaultHeight;
        var target = primary;
        double left = primary.Left + (primary.Width - width) / 2;
        double top = primary.Top + (primary.Height - height) / 2;
        if (settings.RememberWindowPosition && settings.WindowLeft is { } x && settings.WindowTop is { } y)
        {
            // Require a usable section of the title bar; a one-pixel intersection cannot rescue a window.
            var title = new Rect(x, y, width, 48);
            var visible = workAreas.Select(area => (Area: area, Visible: Rect.Intersect(area, title)))
                .Where(item => !item.Visible.IsEmpty && item.Visible.Width >= 80 && item.Visible.Height >= 24)
                .OrderByDescending(item => item.Visible.Width * item.Visible.Height).ToList();
            if (visible.Count > 0) { target = visible[0].Area; left = x; top = y; }
        }
        width = Math.Min(width, target.Width); height = Math.Min(height, target.Height);
        left = Math.Clamp(left, target.Left, Math.Max(target.Left, target.Right - width));
        top = Math.Clamp(top, target.Top, Math.Max(target.Top, target.Bottom - height));
        return new Rect(left, top, width, height);
    }

    public static void Restore(Window window, AppSettings settings)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        var source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var areas = new List<Rect>();
        Rect primary = SystemParameters.WorkArea;
        MonitorCallback callback = (IntPtr monitor, IntPtr hdc, ref NativeRect rect, IntPtr data) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                var start = transform.Transform(new Point(info.Work.Left, info.Work.Top));
                var end = transform.Transform(new Point(info.Work.Right, info.Work.Bottom));
                var area = new Rect(start, end); areas.Add(area);
                if ((info.Flags & 1) != 0) primary = area;
            }
            return true;
        };
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        if (areas.Count == 0) areas.Add(primary);
        var bounds = Resolve(settings, areas, primary, window.Width, window.Height);
        window.MinWidth = Math.Min(window.MinWidth, bounds.Width);
        window.MinHeight = Math.Min(window.MinHeight, bounds.Height);
        window.Width = bounds.Width; window.Height = bounds.Height;
        window.Left = bounds.Left; window.Top = bounds.Top;
    }

    public static AppSettings Capture(AppSettings current, Window window)
    {
        var bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight) : window.RestoreBounds;
        if (bounds.IsEmpty) return current;
        return current with
        {
            WindowWidth = current.RememberWindowSize ? bounds.Width : current.WindowWidth,
            WindowHeight = current.RememberWindowSize ? bounds.Height : current.WindowHeight,
            WindowLeft = current.RememberWindowPosition ? bounds.Left : current.WindowLeft,
            WindowTop = current.RememberWindowPosition ? bounds.Top : current.WindowTop
        };
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public int Flags; }
    private delegate bool MonitorCallback(IntPtr monitor, IntPtr hdc, ref NativeRect rect, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
