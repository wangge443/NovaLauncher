using System.Diagnostics;
using NovaLauncher.App.Models;
using NovaLauncher.App.Persistence;

namespace NovaLauncher.App.Services;

public sealed record ApplicationIconResult(bool Succeeded, string? Path = null, byte[]? Icon = null, string? Error = null);
public interface IApplicationManagementService
{
    Task<string?> RenameAsync(Guid id, string name);
    Task<ApplicationIconResult> ChangeIconAsync(LauncherApplication application, string source);
    Task<ApplicationIconResult> RestoreIconAsync(LauncherApplication application);
    Task<string?> RemoveAsync(LauncherApplication application);
}

public sealed class ApplicationManagementService(IApplicationManagementRepository repository, ApplicationIconCache cache, CustomIconStore custom) : IApplicationManagementService
{
    public static string? ValidateName(string name) => name.Trim().Length switch
    {
        0 => "请输入应用名称",
        > 48 => "应用名称不能超过48个字符",
        _ => null
    };

    public async Task<string?> RenameAsync(Guid id, string name)
    {
        if (ValidateName(name) is { } error) return error;
        try { await repository.RenameAsync(id, name.Trim(), CancellationToken.None); return null; }
        catch (Exception ex) { Trace.TraceError("Application rename failed: {0}", ex); return "名称保存失败"; }
    }

    public async Task<ApplicationIconResult> ChangeIconAsync(LauncherApplication application, string source)
    {
        if (!CustomIconStore.Supports(source)) return new(false, Error: "不支持此图片格式");
        byte[] icon;
        try { icon = await custom.DecodeAsync(source); }
        catch (Exception ex) { Trace.TraceWarning("Custom icon decode failed: {0}", ex); return new(false, Error: "无法读取该图片"); }
        string? candidate = null;
        try
        {
            candidate = await custom.SaveAsync(application.Id, icon);
            await repository.SetCustomIconAsync(application.Id, candidate, CancellationToken.None);
            string saved = candidate; candidate = null;
            await Task.Run(() => custom.DeleteOwned(application.Id, application.CustomIconPath));
            return new(true, saved, icon);
        }
        catch (Exception ex) { Trace.TraceError("Custom icon save failed: {0}", ex); return new(false, Error: "图标保存失败"); }
        finally { if (candidate is not null) await Task.Run(() => custom.DeleteOwned(application.Id, candidate)); }
    }

    public async Task<ApplicationIconResult> RestoreIconAsync(LauncherApplication application)
    {
        try
        {
            byte[]? icon = await cache.LoadAsync(application.Id, application.ExecutablePath, cache.PathFor(application.Id), CancellationToken.None);
            await repository.SetCustomIconAsync(application.Id, null, CancellationToken.None);
            await Task.Run(() => custom.DeleteOwned(application.Id, application.CustomIconPath));
            return new(true, Icon: icon);
        }
        catch (Exception ex) { Trace.TraceError("Restore icon failed: {0}", ex); return new(false, Error: "图标保存失败"); }
    }

    public async Task<string?> RemoveAsync(LauncherApplication application)
    {
        try { await repository.RemoveAsync(application.Id, CancellationToken.None); }
        catch (Exception ex) { Trace.TraceError("Application removal failed: {0}", ex); return "无法移除该应用"; }
        // Cleanup receives only managed resource identifiers, never ExecutablePath or SourcePath.
        await Task.Run(() => { cache.Delete(application.Id); custom.DeleteOwned(application.Id, application.CustomIconPath); });
        return null;
    }
}
