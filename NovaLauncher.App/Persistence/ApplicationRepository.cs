using System.IO;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace NovaLauncher.App.Persistence;

public interface IApplicationRepository
{
    Task InitializeAsync(CancellationToken token);
    Task<IReadOnlyList<ApplicationEntity>> GetAllAsync(CancellationToken token);
    Task AddAsync(ApplicationEntity application, CancellationToken token);
    Task UpdateAsync(Guid id, long launchCount, DateTimeOffset? lastLaunchTime, CancellationToken token);
    Task<bool> ExistsAsync(string executablePath, CancellationToken token);
    Task UpdateCategoryAsync(Guid id, Guid? categoryId, CancellationToken token);
}

public sealed class ApplicationRepository(IDbContextFactory<LauncherDbContext> factory, ApplicationDataPaths paths)
    : IApplicationRepository
{
    public static string PathKey(string path) => Path.GetFullPath(path).ToUpperInvariant();
    private readonly DatabaseInitializationService _initialization = new(factory, paths);

    // SQLite's async APIs still perform synchronous I/O. Keep the whole operation off the UI thread.
    public Task InitializeAsync(CancellationToken token) => _initialization.InitializeAsync(token);

    public Task<IReadOnlyList<ApplicationEntity>> GetAllAsync(CancellationToken token) => Task.Run<IReadOnlyList<ApplicationEntity>>(async () =>
    {
        using var db = factory.CreateDbContext();
        try
        {
            return await db.Applications.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync(token);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or ArgumentException)
        {
            // An externally damaged value must not prevent healthy rows from loading.
            Trace.TraceWarning("Invalid database value; recovering readable application rows: {0}", ex.GetType().Name);
            var rows = await db.Database.SqlQueryRaw<long>("SELECT rowid AS Value FROM Applications").ToListAsync(token);
            var recovered = new List<ApplicationEntity>();
            foreach (long row in rows)
            {
                try
                {
                    recovered.Add(await db.Applications.FromSqlInterpolated($"SELECT * FROM Applications WHERE rowid = {row}")
                        .AsNoTracking().SingleAsync(token));
                }
                catch (Exception invalid) when (invalid is FormatException or InvalidCastException or ArgumentException)
                {
                    Trace.TraceWarning("Skipping invalid application row {0}: {1}", row, invalid.GetType().Name);
                }
            }
            return recovered.OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToList();
        }
    }, token);

    public Task<bool> ExistsAsync(string executablePath, CancellationToken token) => Task.Run(async () =>
    {
        string key = PathKey(executablePath);
        using var db = factory.CreateDbContext();
        return await db.Applications.AnyAsync(x => x.TargetPathKey == key, token);
    }, token);

    public Task AddAsync(ApplicationEntity application, CancellationToken token) => Task.Run(async () =>
    {
        using var db = factory.CreateDbContext();
        application.TargetPathKey = PathKey(application.ExecutablePath);
        application.SortOrder = (await db.Applications.MaxAsync(x => (long?)x.SortOrder, token) ?? -1) + 1;
        db.Applications.Add(application);
        await db.SaveChangesAsync(token);
    }, token);

    public Task UpdateCategoryAsync(Guid id, Guid? categoryId, CancellationToken token) => Task.Run(async () =>
    {
        using var db = factory.CreateDbContext();
        if (categoryId.HasValue && !await db.Categories.AnyAsync(x => x.Id == categoryId, token))
            throw new InvalidOperationException("Category does not exist.");
        int changed = await db.Applications.Where(x => x.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.CategoryId, categoryId), token);
        if (changed != 1) throw new InvalidOperationException("Application does not exist.");
    }, token);

    public Task UpdateAsync(Guid id, long launchCount, DateTimeOffset? lastLaunchTime, CancellationToken token) => Task.Run(async () =>
    {
        using var db = factory.CreateDbContext();
        // Update statistics only; an older async snapshot must never overwrite newer statistics or metadata.
        await db.Applications.Where(x => x.Id == id && x.LaunchCount < launchCount)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.LaunchCount, launchCount)
                .SetProperty(x => x.LastLaunchTime, lastLaunchTime), token);
    }, token);
}
