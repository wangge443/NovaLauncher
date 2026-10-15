using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace NovaLauncher.App.Services;

public sealed class ShortcutResolver : IShortcutResolver
{
    public string Resolve(string shortcutPath)
    {
        // No Resolve call: do not search for moved targets, display dialogs, or update the .lnk.
        object link = new ShellLink();
        try
        {
            ((IPersistFile)link).Load(shortcutPath, 0);
            var path = new StringBuilder(32768);
            ((IShellLinkW)link).GetPath(path, path.Capacity, IntPtr.Zero, 4 /* SLGP_RAWPATH */);
            return Environment.ExpandEnvironmentVariables(path.ToString());
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    // Only the first vtable method is used; IPersistFile is obtained via QueryInterface.
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path,
            int capacity, IntPtr findData, uint flags);
    }
}
