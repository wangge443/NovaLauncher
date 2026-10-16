using Microsoft.EntityFrameworkCore;
using NovaLauncher.App.Models;

namespace NovaLauncher.App.Persistence;

public interface ICategoryRepository
{
    Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken token);
    Task AddAsync(Category category, CancellationToken token);
    Task<bool> ExistsByNameAsync(string name, CancellationToken token);
}

// The application initializes the shared database before loading categories.
public sealed class CategoryRepository(IDbContextFactory<LauncherDbContext> factory) : ICategoryRepository
{
    public Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken token) => Task.Run<IReadOnlyList<Category>>(async () =>
    {
        using var db = factory.CreateDbContext();
        return await db.Categories.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync(token);
    }, token);

    public Task<bool> ExistsByNameAsync(string name, CancellationToken token) => Task.Run(async () =>
    {
        using var db = factory.CreateDbContext();
        return await db.Categories.AnyAsync(x => x.Name == name, token);
    }, token);

    public Task AddAsync(Category category, CancellationToken token) => Task.Run(async () =>
    {
        using var db = factory.CreateDbContext();
        category.SortOrder = (await db.Categories.MaxAsync(x => (long?)x.SortOrder, token) ?? -1) + 1;
        db.Categories.Add(category);
        await db.SaveChangesAsync(token);
    }, token);
}
