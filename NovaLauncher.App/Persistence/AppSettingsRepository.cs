using Microsoft.EntityFrameworkCore;
using NovaLauncher.App.Models;

namespace NovaLauncher.App.Persistence;

public record AppSettingsEntity : AppSettings;

public interface IAppSettingsRepository
{
    Task<AppSettings> GetOrCreateAsync(CancellationToken token = default);
    Task SaveAsync(AppSettings settings, CancellationToken token = default);
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

    public Task SaveAsync(AppSettings settings, CancellationToken token = default) => Task.Run(async () =>
    {
        using var db = factory.CreateDbContext();
        var entity = await db.AppSettings.SingleAsync(x => x.Id == 1, token);
        db.Entry(entity).CurrentValues.SetValues(settings);
        await db.SaveChangesAsync(token);
    }, token);
}
