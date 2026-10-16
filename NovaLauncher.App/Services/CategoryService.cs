using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NovaLauncher.App.Models;
using NovaLauncher.App.Persistence;

namespace NovaLauncher.App.Services;

public sealed record CategoryResult(Category? Category, string? Error = null);
public interface ICategoryService
{
    Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken token);
    Task<CategoryResult> CreateAsync(string name, CancellationToken token);
}

public sealed class CategoryService(ICategoryRepository repository) : ICategoryService
{
    public static string? ValidateName(string name) => name.Trim().Length switch
    {
        0 => "请输入分类名称",
        > 24 => "分类名称不能超过24个字符",
        _ => null
    };

    public Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken token) => repository.GetAllAsync(token);

    public async Task<CategoryResult> CreateAsync(string name, CancellationToken token)
    {
        name = name.Trim();
        if (ValidateName(name) is { } error) return new(null, error);
        try
        {
            if (await repository.ExistsByNameAsync(name, token)) return new(null, "该分类已存在");
            var category = new Category { Id = Guid.NewGuid(), Name = name, CreatedAt = DateTimeOffset.UtcNow };
            await repository.AddAsync(category, token);
            return new(category);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 })
        {
            return new(null, "该分类已存在");
        }
        catch (Exception ex)
        {
            Trace.TraceError("Category save failed: {0}", ex);
            return new(null, "分类保存失败");
        }
    }
}
