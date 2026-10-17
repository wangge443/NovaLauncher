using Microsoft.EntityFrameworkCore;
using NovaLauncher.App.Models;

namespace NovaLauncher.App.Persistence;

public sealed class UserProfileEntity
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = "User";
    public string? AvatarPath { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public interface IUserProfileRepository
{
    Task<UserProfile> GetOrCreateAsync(string defaultName);
    Task<UserProfile> SetNameAsync(string name);
    Task<UserProfile> SetAvatarAsync(string? path);
}

// The shared database lifecycle runs before this repository. Each write changes only its own field.
public sealed class UserProfileRepository(IDbContextFactory<LauncherDbContext> factory) : IUserProfileRepository
{
    private static UserProfile Map(UserProfileEntity entity) => new(entity.Id, entity.DisplayName, entity.AvatarPath, entity.UpdatedAt);
    public Task<UserProfile> GetOrCreateAsync(string defaultName) => Task.Run(async () =>
    {
        using var db = factory.CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT OR IGNORE INTO UserProfiles (Id, DisplayName, AvatarPath, UpdatedAt) VALUES (1, {defaultName}, NULL, {now});");
        return Map(await db.UserProfiles.AsNoTracking().SingleAsync(x => x.Id == 1));
    });
    public Task<UserProfile> SetNameAsync(string name) => UpdateAsync(entity => entity.DisplayName = name);
    public Task<UserProfile> SetAvatarAsync(string? path) => UpdateAsync(entity => entity.AvatarPath = path);
    private Task<UserProfile> UpdateAsync(Action<UserProfileEntity> update) => Task.Run(async () =>
    {
        using var db = factory.CreateDbContext();
        var entity = await db.UserProfiles.SingleAsync(x => x.Id == 1);
        update(entity);
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return Map(entity);
    });
}
