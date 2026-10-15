using NovaLauncher.App.Models;

namespace NovaLauncher.App.Services;

public sealed record ApplicationImportResult(LauncherApplication? Application, string? Error);

public interface IApplicationImportService
{
    Task<ApplicationImportResult> ImportAsync(string sourcePath, CancellationToken cancellationToken);
}

public interface IShortcutResolver
{
    string Resolve(string shortcutPath);
}

public interface IApplicationIconService
{
    byte[]? Extract(string executablePath);
}
