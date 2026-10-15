using NovaLauncher.App.Models;

namespace NovaLauncher.App.Services;

public sealed record ApplicationLaunchResult(bool Succeeded, string? Error = null);

public interface IApplicationLaunchService
{
    Task<ApplicationLaunchResult> LaunchAsync(LauncherApplication application, CancellationToken cancellationToken = default);
}
