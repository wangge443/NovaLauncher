using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NovaLauncher.App.Persistence;

namespace NovaLauncher.App.Services;

public interface IBackgroundFilePicker { string? Pick(); }
public sealed class BackgroundFilePicker : IBackgroundFilePicker
{
    public string? Pick()
    {
        var dialog = new OpenFileDialog { Title = "选择背景图片", Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp", CheckFileExists = true };
        return dialog.ShowDialog(System.Windows.Application.Current.MainWindow) == true ? dialog.FileName : null;
    }
}
public interface IDesktopWallpaperSource { Task<string> GetPathAsync(); }
public sealed class DesktopWallpaperSource : IDesktopWallpaperSource
{
    // Primary/system wallpaper only: no hooks and no DWM transparency. Refresh is explicit.
    public Task<string> GetPathAsync() => Task.Run(() =>
    {
        var path = new StringBuilder(260);
        if (!SystemParametersInfo(0x0073, (uint)path.Capacity, path, 0) || path.Length == 0)
            throw new IOException("Desktop wallpaper is unavailable.");
        string requested = path.ToString();
        if (File.Exists(requested)) return requested;
        // Wallpaper managers can leave stale API paths. Windows retains its last static transcoded image.
        string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Themes", "TranscodedWallpaper");
        if (File.Exists(cache)) return cache;
        throw new FileNotFoundException("Neither the system wallpaper nor its static cache exists.");
    });
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint count, StringBuilder value, uint flags);
}
public sealed class BackgroundImageStore(ApplicationDataPaths paths)
{
    public static bool Supports(string path) => new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    public Task<BitmapSource> LoadAsync(string path) => Task.Run(() => Load(path));
    private static BitmapSource Load(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 80 * 1024 * 1024) throw new InvalidDataException("Image too large.");
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapSource source = decoder.Frames[0];
        if ((long)source.PixelWidth * source.PixelHeight > 40_000_000) throw new InvalidDataException("Image dimensions too large.");
        double scale = Math.Min(1, 2560d / Math.Max(source.PixelWidth, source.PixelHeight));
        if (scale < 1) source = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        source.Freeze();
        return source;
    }
    public Task<(string Path, BitmapSource Image)> ImportAsync(string path) => Task.Run(() =>
    {
        var image = Load(path);
        Directory.CreateDirectory(paths.BackgroundDirectory);
        string final = Path.Combine(paths.BackgroundDirectory, $"background-{Guid.NewGuid():N}.png");
        string temporary = final + ".tmp";
        try
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { encoder.Save(stream); stream.Flush(true); }
            File.Move(temporary, final);
            return (final, image);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    });
    public void DeleteOwned(string? path)
    {
        if (path is null) return;
        try
        {
            var full = Path.GetFullPath(path);
            if (string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(paths.BackgroundDirectory), StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(full).StartsWith("background-", StringComparison.Ordinal) && Path.GetExtension(full) == ".png") File.Delete(full);
        }
        catch (Exception ex) { Trace.TraceWarning("Background cleanup failed: {0}", ex.Message); }
    }
}
