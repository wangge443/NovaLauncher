using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NovaLauncher.App.Persistence;

namespace NovaLauncher.App.Services;

public interface IApplicationIconPicker { string? Pick(); }
public sealed class ApplicationIconPicker : IApplicationIconPicker
{
    public string? Pick()
    {
        var dialog = new OpenFileDialog { Title = "更换应用图标", Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp;*.ico)|*.png;*.jpg;*.jpeg;*.bmp;*.ico", CheckFileExists = true };
        return dialog.ShowDialog(System.Windows.Application.Current.MainWindow) == true ? dialog.FileName : null;
    }
}

public sealed class CustomIconStore(ApplicationDataPaths paths)
{
    public static bool Supports(string path) => new[] { ".png", ".jpg", ".jpeg", ".bmp", ".ico" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static byte[] Decode(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 50 * 1024 * 1024) throw new InvalidDataException("Icon file too large.");
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapSource image = decoder.Frames.OrderByDescending(x => (long)x.PixelWidth * x.PixelHeight).First();
        if ((long)image.PixelWidth * image.PixelHeight > 32_000_000) throw new InvalidDataException("Icon dimensions too large.");
        double scale = Math.Min(1, 256d / Math.Max(image.PixelWidth, image.PixelHeight));
        if (scale < 1) image = new TransformedBitmap(image, new ScaleTransform(scale, scale));
        image.Freeze();
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = new MemoryStream(); encoder.Save(output); return output.ToArray();
    }

    public Task<byte[]> DecodeAsync(string source) => Task.Run(() => Decode(source));

    public Task<string> SaveAsync(Guid id, byte[] png) => Task.Run(() =>
    {
        Directory.CreateDirectory(paths.CustomIconsDirectory);
        string final = Path.Combine(paths.CustomIconsDirectory, $"{id:N}-{Guid.NewGuid():N}.png");
        string temporary = final + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(png); file.Flush(true); }
            File.Move(temporary, final);
            return final;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) { Trace.TraceWarning("Custom icon temporary cleanup failed: {0}", ex); }
        }
    });

    private bool IsOwned(Guid id, string path)
    {
        string full = Path.GetFullPath(path);
        string name = Path.GetFileName(full);
        return string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(paths.CustomIconsDirectory), StringComparison.OrdinalIgnoreCase)
            && name.StartsWith(id.ToString("N") + "-", StringComparison.OrdinalIgnoreCase)
            && name.Length == 69 && name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParseExact(name.Substring(33, 32), "N", out _);
    }

    public Task<byte[]?> TryLoadAsync(Guid id, string? path, CancellationToken token) => Task.Run<byte[]?>(() =>
    {
        if (path is null) return null;
        try { return IsOwned(id, path) && File.Exists(path) ? Decode(path) : null; }
        catch (Exception ex) { Trace.TraceWarning("Custom icon unavailable, falling back: {0}", ex); return null; }
    }, token);

    public void DeleteOwned(Guid id, string? path)
    {
        if (path is null) return;
        try { if (IsOwned(id, path)) File.Delete(path); }
        catch (Exception ex) { Trace.TraceWarning("Custom icon cleanup failed: {0}", ex); }
    }
}
