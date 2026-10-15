using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace NovaLauncher.App.Services;

public sealed class ApplicationIconService : IApplicationIconService
{
    public byte[]? Extract(string executablePath)
    {
        IntPtr icon = IntPtr.Zero;
        try
        {
            // Request 256 physical pixels, preserving embedded high-resolution icon resources.
            int result = SHDefExtractIcon(executablePath, 0, 0, out icon, IntPtr.Zero, 256);
            if (result != 0 || icon == IntPtr.Zero)
            {
                Trace.TraceInformation("No executable icon available (HRESULT {0:X8}).", result);
                return null;
            }

            var bitmap = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or COMException or NotSupportedException or InvalidOperationException)
        {
            Trace.TraceWarning("Icon extraction failed: {0}", ex.GetType().Name);
            return null;
        }
        finally
        {
            if (icon != IntPtr.Zero)
            {
                DestroyIcon(icon);
            }
        }
    }

    [DllImport("shell32.dll", EntryPoint = "SHDefExtractIconW", CharSet = CharSet.Unicode)]
    private static extern int SHDefExtractIcon(string path, int index, uint flags,
        out IntPtr largeIcon, IntPtr smallIcon, uint size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
