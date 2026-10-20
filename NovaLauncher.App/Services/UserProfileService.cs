using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NovaLauncher.App.Models;
using NovaLauncher.App.Persistence;

namespace NovaLauncher.App.Services;

public sealed record ProfileResult(UserProfile? Profile, byte[]? Avatar, string? Error = null);
public interface IUserProfileService
{
    Task<ProfileResult> LoadAsync();
    Task<ProfileResult> SaveNameAsync(string name);
    Task<ProfileResult> ChangeAvatarAsync(string path);
    Task<ProfileResult> ResetAvatarAsync();
}

public sealed class UserProfileService(IUserProfileRepository repository, ApplicationDataPaths paths) : IUserProfileService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private UserProfile? _current;
    private byte[]? _avatar;
    public static string DefaultName()
    {
        try { string name = Environment.UserName.Trim(); return name.Length is >= 1 and <= 24 ? name : "User"; }
        catch (Exception ex) { Trace.TraceWarning("Profile username unavailable: {0}", ex); return "User"; }
    }
    public async Task<ProfileResult> LoadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _current = await repository.GetOrCreateAsync(DefaultName());
            _avatar = null;
            if (_current.AvatarPath is { } path)
            {
                try { _avatar = await Task.Run(() => ReadPng(path)); }
                catch (Exception ex) { Trace.TraceWarning("Profile avatar unavailable: {0}", ex); }
            }
            return new(_current, _avatar);
        }
        catch (Exception ex) { Trace.TraceError("Profile load failed: {0}", ex); return new(null, null, "个人资料读取失败"); }
        finally { _gate.Release(); }
    }
    public async Task<ProfileResult> SaveNameAsync(string name)
    {
        name = name.Trim();
        if (name.Length == 0) return new(null, null, "名称不能为空");
        if (name.Length > 24) return new(null, null, "名称过长");
        await _gate.WaitAsync();
        try { _current = await repository.SetNameAsync(name); return new(_current, _avatar); }
        catch (Exception ex) { Trace.TraceError("Profile name save failed: {0}", ex); return new(null, null, "名称保存失败"); }
        finally { _gate.Release(); }
    }
    public async Task<ProfileResult> ChangeAvatarAsync(string path)
    {
        if (!new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            return new(null, null, "不支持此图片格式");
        byte[] image;
        try { image = await Task.Run(() => ReadPng(path)); }
        catch (Exception ex) { Trace.TraceWarning("Avatar decoding failed: {0}", ex); return new(null, null, "无法读取该图片"); }
        await _gate.WaitAsync();
        string? created = null;
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(paths.ProfileDirectory);
            // Immutable generations make the file + SQLite update failure-safe without overwriting the old avatar.
            created = Path.Combine(paths.ProfileDirectory, $"avatar-{Guid.NewGuid():N}.png");
            temporary = created + ".tmp";
            await File.WriteAllBytesAsync(temporary, image);
            File.Move(temporary, created);
            string? old = _current?.AvatarPath;
            _current = await repository.SetAvatarAsync(created);
            _avatar = image;
            DeleteOwned(old);
            return new(_current, _avatar);
        }
        catch (Exception ex)
        {
            Trace.TraceError("Profile avatar save failed: {0}", ex);
            DeleteOwned(created); DeleteOwned(temporary);
            return new(null, null, "头像保存失败");
        }
        finally { _gate.Release(); }
    }
    public async Task<ProfileResult> ResetAvatarAsync()
    {
        await _gate.WaitAsync();
        try
        {
            string? old = _current?.AvatarPath;
            _current = await repository.SetAvatarAsync(null);
            _avatar = null;
            DeleteOwned(old);
            return new(_current, null);
        }
        catch (Exception ex) { Trace.TraceError("Profile avatar reset failed: {0}", ex); return new(null, null, "头像保存失败"); }
        finally { _gate.Release(); }
    }
    private void DeleteOwned(string? path)
    {
        if (path is null) return;
        try
        {
            string full = Path.GetFullPath(path);
            if (string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(paths.ProfileDirectory), StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(full).StartsWith("avatar-", StringComparison.Ordinal)) File.Delete(full);
        }
        catch (Exception ex) { Trace.TraceWarning("Profile obsolete avatar cleanup failed: {0}", ex); }
    }
    private static byte[] ReadPng(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 50 * 1024 * 1024) throw new InvalidDataException("Image too large");
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapSource source = decoder.Frames[0];
        if ((long)source.PixelWidth * source.PixelHeight > 32_000_000) throw new InvalidDataException("Image dimensions too large");
        double scale = Math.Min(1, 512d / Math.Max(source.PixelWidth, source.PixelHeight));
        if (scale < 1) source = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }
}
