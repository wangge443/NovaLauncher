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
        return (AppSettings)await SafeSettings(db).AsNoTracking().SingleAsync(x => x.Id == 1, token);
    }, token);

    private static IQueryable<AppSettingsEntity> SafeSettings(LauncherDbContext db)
    {
        // SQLite permits non-integer values in INTEGER columns. Normalize only the new fields
        // in the read projection, so corrupt hotkey data cannot discard unrelated user settings.
        const string valid = "typeof(GlobalHotkeyEnabled) = 'integer' AND GlobalHotkeyEnabled IN (0,1) AND typeof(GlobalHotkeyModifiers) = 'integer' AND GlobalHotkeyModifiers BETWEEN 1 AND 15 AND typeof(GlobalHotkeyKey) = 'integer' AND GlobalHotkeyKey BETWEEN 1 AND 255";
        var columns = db.Model.FindEntityType(typeof(AppSettingsEntity))!.GetProperties().Select(p => p.Name switch
        {
            nameof(AppSettings.GlobalHotkeyEnabled) => $"CASE WHEN {valid} THEN GlobalHotkeyEnabled ELSE 1 END AS GlobalHotkeyEnabled",
            nameof(AppSettings.GlobalHotkeyModifiers) => $"CASE WHEN {valid} THEN GlobalHotkeyModifiers ELSE 0 END AS GlobalHotkeyModifiers",
            nameof(AppSettings.GlobalHotkeyKey) => $"CASE WHEN {valid} THEN GlobalHotkeyKey ELSE 0 END AS GlobalHotkeyKey",
            _ => '"' + p.Name + '"'
        });
        // Identifiers come only from the compiled EF model, never from input.
        return db.AppSettings.FromSqlRaw("SELECT " + string.Join(",", columns) + " FROM AppSettings");
    }

    public Task<AppSettings> SaveAsync(AppSettings original, AppSettings settings, CancellationToken token = default) => Task.Run(async () =>
    {
        using var db = factory.CreateDbContext();
        // SQLite begins an immediate transaction: reserve the writer before reading,
        // avoiding competing read-to-write lock upgrades across repository instances.
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var safe = await SafeSettings(db).AsNoTracking().SingleAsync(x => x.Id == 1, token);
        if (new GlobalHotkey(safe.GlobalHotkeyModifiers, safe.GlobalHotkeyKey).ValidationError is not null)
        {
            System.Diagnostics.Trace.TraceWarning("GlobalHotkey repairing invalid stored configuration");
            await db.Database.ExecuteSqlRawAsync("UPDATE AppSettings SET GlobalHotkeyEnabled=1, GlobalHotkeyModifiers=3, GlobalHotkeyKey=32 WHERE Id=1", token);
        }
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
