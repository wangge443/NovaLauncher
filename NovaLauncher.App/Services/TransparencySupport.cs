using System.Runtime.InteropServices;
using System.Windows;

namespace NovaLauncher.App.Services;

public static class TransparencySupport
{
    private static bool _sessionDisabled;
    internal static void DisableForSession() => _sessionDisabled = true;
    // Conservative experimental support boundary, not the minimum OS for layered windows.
    public static bool IsSupported()
    {
        if (_sessionDisabled || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621) || SystemParameters.HighContrast ||
            SystemParameters.IsRemoteSession || Environment.GetEnvironmentVariable("NOVA_DISABLE_TRANSPARENCY") == "1") return false;
        try { return DwmIsCompositionEnabled(out bool enabled) >= 0 && enabled; }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);
}
