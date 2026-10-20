using Microsoft.EntityFrameworkCore;
using NovaLauncher.App.Models;

namespace NovaLauncher.App.Persistence;

public record AppSettingsEntity : AppSettings;

public interface IAppSettingsRepository
{
    Task<AppSettings> GetOrCreateAsync(CancellationToken token = default);
    Task<AppSettings> SaveAsync(AppSettings original, AppSettings settings, CancellationToken token = default);
}

public sealed class AppSettingsRepository(IDbContextFactory<LauncherDbContext> factory) : IAppSettingsRepository
{
    public Task<AppSettings> GetOrCreateAsync(CancellationToken token = default) => Task.Run(async () =>
    {
        using var db = factory.CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT OR IGNORE INTO AppSettings (Id, StartupPage, RememberWindowSize, RememberWindowPosition, BackgroundMode, ThemeMode, UpdatedAt) VALUES (1, 'Home', 1, 1, 'Default', 'Light', {now});", token);
        return (AppSettings)await db.AppSettings.AsNoTracking().SingleAsync(x => x.Id == 1, token);
    }, token);

    public Task<AppSettings> SaveAsync(AppSettings original, AppSettings settings, CancellationToken token = default) => Task.Run(async () =>
    {
        using var db = factory.CreateDbContext();
        // SQLite begins an immediate transaction: reserve the writer before reading,
        // avoiding competing read-to-write lock upgrades across repository instances.
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var entity = await db.AppSettings.SingleAsync(x => x.Id == 1, token);
        var entry = db.Entry(entity);
        // Patch only the caller's changes. A stale window snapshot must not reset a newer background.
        foreach (var property in entry.Properties)
        {
            if (property.Metadata.IsPrimaryKey()) continue;
            var accessor = property.Metadata.PropertyInfo!;
            object? before = accessor.GetValue(original);
            object? after = accessor.GetValue(settings);
            if (!Equals(before, after)) property.CurrentValue = after;
        }
        await db.SaveChangesAsync(token);
        await entry.ReloadAsync(token);
        await transaction.CommitAsync(token);
        return (AppSettings)entity;
    }, token);
}
