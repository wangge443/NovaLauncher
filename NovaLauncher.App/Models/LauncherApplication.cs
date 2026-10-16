namespace NovaLauncher.App.Models;

public enum ApplicationSourceType { Executable, Shortcut }

// PNG bytes keep the business model independent of WPF and native handles.
public sealed record LauncherApplication(
    Guid Id, string DisplayName, string ExecutablePath, string SourcePath,
    byte[]? Icon, ApplicationSourceType SourceType)
{
    public bool IsAvailable { get; init; } = true;
    public Guid? CategoryId { get; init; }
    public long LaunchCount { get; init; }
    public DateTimeOffset? LastLaunchTime { get; init; }
}
