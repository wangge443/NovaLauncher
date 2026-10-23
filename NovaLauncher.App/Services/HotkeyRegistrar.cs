using System.Runtime.InteropServices;
using System.Windows.Interop;
using NovaLauncher.App.Models;

namespace NovaLauncher.App.Services;

public interface IHotkeyRegistrar : IDisposable
{
    event Action<int>? Pressed;
    bool Register(int id, GlobalHotkey hotkey, out int error);
    void Unregister(int id);
}

// A message-only HWND on the UI dispatcher survives Glass/main-window host replacement.
// Created only after the primary MainWindow has a HWND; never instantiated by secondary instances.
public sealed class HotkeyRegistrar : IHotkeyRegistrar
{
    private readonly HwndSource _source;
    private readonly HashSet<int> _ids = [];
    public event Action<int>? Pressed;
    public HotkeyRegistrar()
    {
        _source = new HwndSource(new HwndSourceParameters("Nova Global Hotkey")
        { ParentWindow = new IntPtr(-3), WindowStyle = 0, Width = 0, Height = 0 });
        _source.AddHook(Messages);
    }
    public bool Register(int id, GlobalHotkey hotkey, out int error)
    {
        bool registered = RegisterHotKey(_source.Handle, id, (uint)hotkey.Modifiers | 0x4000, (uint)hotkey.Key);
        error = registered ? 0 : Marshal.GetLastWin32Error();
        if (registered) _ids.Add(id);
        return registered;
    }
    public void Unregister(int id)
    {
        if (!_ids.Remove(id)) return;
        if (!UnregisterHotKey(_source.Handle, id))
            System.Diagnostics.Trace.TraceWarning("GlobalHotkey Unregister failure code={0}", Marshal.GetLastWin32Error());
    }
    private IntPtr Messages(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312 && _ids.Contains(wParam.ToInt32()))
        { handled = true; Pressed?.Invoke(wParam.ToInt32()); }
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        foreach (int id in _ids.ToArray()) Unregister(id);
        _source.RemoveHook(Messages); _source.Dispose();
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
