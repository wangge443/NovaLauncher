using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NovaLauncher.App.Models;
using NovaLauncher.App.Persistence;

namespace NovaLauncher.App.Services;

public sealed record ApplicationLoadResult(IReadOnlyList<LauncherApplication> Applications, string? Error = null);
public interface IApplicationPersistenceService
{
    Task<ApplicationLoadResult> LoadAsync(CancellationToken token);
    Task<string?> AddAsync(LauncherApplication application, CancellationToken token);
    Task SaveStatisticsAsync(LauncherApplication application);
    Task<string?> SetCategoryAsync(Guid applicationId, Guid? categoryId, CancellationToken token);
    Task<string?> SetFavoriteAsync(Guid applicationId, bool isFavorite, CancellationToken token);
    Task FlushAsync();
}

public sealed class ApplicationPersistenceService(
    IApplicationRepository repository, ApplicationIconCache cache) : IApplicationPersistenceService
{
    private readonly object _sync = new();
    private readonly HashSet<Task> _writes = [];

    public async Task<ApplicationLoadResult> LoadAsync(CancellationToken token)
    {
        var applications = new List<LauncherApplication>();
        try
        {
            await repository.InitializeAsync(token);
            await cache.InitializeAsync(token);
            var records = await repository.GetAllAsync(token);
            foreach (var record in records)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (!Enum.IsDefined((ApplicationSourceType)record.SourceType) ||
                        !Path.IsPathFullyQualified(record.ExecutablePath) || !Path.IsPathFullyQualified(record.SourcePath))
                        throw new InvalidDataException("Invalid application paths or source type.");
                    var icon = await cache.CustomIcons.TryLoadAsync(record.Id, record.CustomIconPath, token)
                        ?? await cache.LoadAsync(record.Id, record.ExecutablePath, record.IconCachePath, token);
                    bool available = await Task.Run(() => File.Exists(record.ExecutablePath), token);
                    applications.Add(new(record.Id, record.DisplayName, record.ExecutablePath, record.SourcePath,
                        icon, (ApplicationSourceType)record.SourceType)
                    {
                        LaunchCount = record.LaunchCount, LastLaunchTime = record.LastLaunchTime,
                        IsAvailable = available, CategoryId = record.CategoryId, IsFavorite = record.IsFavorite,
                        CustomIconPath = record.CustomIconPath
                    });
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex) { Trace.TraceWarning("Application record {0} failed: {1}", record.Id, ex.GetType().Name); }
            }
            return new(applications, applications.Count != records.Count ? "部分应用记录无法读取" : null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (DatabaseInitializationException ex)
        {
            return new(applications, ex.Message);
        }
        catch (Exception ex)
        {
            Trace.TraceError("Application database load failed: {0}", ex);
            return new(applications, "无法读取应用数据库，请检查数据目录权限或数据库文件");
        }
    }

    public async Task<string?> AddAsync(LauncherApplication application, CancellationToken token)
    {
        try
        {
            await repository.InitializeAsync(token);
            if (await repository.ExistsAsync(application.ExecutablePath, token)) return "该应用已添加";
            // The deterministic path is stored even if the cache write fails, so a later load can repair it.
            await repository.AddAsync(new ApplicationEntity
            {
                Id = application.Id, DisplayName = application.DisplayName, ExecutablePath = application.ExecutablePath,
                SourcePath = application.SourcePath, SourceType = (int)application.SourceType,
                LaunchCount = application.LaunchCount, LastLaunchTime = application.LastLaunchTime,
                DateAdded = DateTimeOffset.UtcNow, IconCachePath = cache.PathFor(application.Id),
                CategoryId = application.CategoryId, IsFavorite = application.IsFavorite
            }, token);
            await cache.WriteAsync(application.Id, application.Icon, CancellationToken.None);
            return null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 })
        {
            return "该应用已添加";
        }
        catch (DatabaseInitializationException ex) { return ex.Message; }
        catch (Exception ex)
        {
            Trace.TraceError("Application save failed: {0}", ex);
            return "无法保存应用，请检查数据目录权限或数据库文件";
        }
    }

    public Task SaveStatisticsAsync(LauncherApplication application)
    {
        lock (_sync)
        {
            Task write = SaveCoreAsync(application);
            _writes.Add(write);
            _ = write.ContinueWith(completed => { lock (_sync) _writes.Remove(completed); }, TaskScheduler.Default);
            return write;
        }
    }

    public async Task<string?> SetCategoryAsync(Guid applicationId, Guid? categoryId, CancellationToken token)
    {
        try
        {
            await repository.UpdateCategoryAsync(applicationId, categoryId, token);
            return null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Trace.TraceError("Application category update failed: {0}", ex);
            return "分类修改失败";
        }
    }

    public async Task<string?> SetFavoriteAsync(Guid applicationId, bool isFavorite, CancellationToken token)
    {
        try
        {
            await repository.UpdateFavoriteAsync(applicationId, isFavorite, token);
            return null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Trace.TraceError("Application favorite update failed: {0}", ex);
            return "收藏状态保存失败";
        }
    }

    private async Task SaveCoreAsync(LauncherApplication application)
    {
        try { await repository.UpdateAsync(application.Id, application.LaunchCount, application.LastLaunchTime, CancellationToken.None); }
        catch (Exception ex) { Trace.TraceError("Launch statistics save failed: {0}", ex); }
    }

    public Task FlushAsync() { lock (_sync) return Task.WhenAll(_writes.ToArray()); }
}

public sealed class ApplicationIconCache(ApplicationDataPaths paths, IApplicationIconService icons)
{
    public CustomIconStore CustomIcons { get; } = new(paths);
    public string PathFor(Guid id) => paths.IconPath(id);
    public void Delete(Guid id)
    {
        try { File.Delete(PathFor(id)); }
        catch (Exception ex) { Trace.TraceWarning("Icon cache cleanup failed: {0}", ex); }
    }

    public Task InitializeAsync(CancellationToken token) => Task.Run(() =>
    {
        try { Directory.CreateDirectory(paths.IconsDirectory); }
        catch (Exception ex) { Trace.TraceWarning("Icon cache directory unavailable: {0}", ex.GetType().Name); }
    }, token);

    public Task<byte[]?> LoadAsync(Guid id, string executablePath, string? storedPath, CancellationToken token) => Task.Run(() =>
    {
        string path = PathFor(id);
        try
        {
            // Never follow an arbitrary path from a modified database outside the managed cache.
            if (string.Equals(path, storedPath, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            {
                byte[] bytes = File.ReadAllBytes(path);
                Validate(bytes);
                return bytes;
            }
        }
        catch (Exception ex) { Trace.TraceWarning("Icon cache read failed: {0}", ex.GetType().Name); }
        token.ThrowIfCancellationRequested();
        try
        {
            byte[]? extracted = icons.Extract(executablePath);
            Write(id, extracted);
            return extracted;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Icon recovery failed: {0}", ex.GetType().Name);
            return null;
        }
    }, token);

    public Task WriteAsync(Guid id, byte[]? bytes, CancellationToken token) => Task.Run(() => Write(id, bytes), token);

    private void Write(Guid id, byte[]? bytes)
    {
        if (bytes is not { Length: > 0 }) return;
        try
        {
            Validate(bytes);
            Directory.CreateDirectory(paths.IconsDirectory);
            File.WriteAllBytes(PathFor(id), bytes);
        }
        catch (Exception ex) { Trace.TraceWarning("Icon cache write failed: {0}", ex.GetType().Name); }
    }

    private static void Validate(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        foreach (var frame in decoder.Frames) frame.Freeze();
    }
}
