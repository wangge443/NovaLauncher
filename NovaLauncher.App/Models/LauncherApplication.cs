namespace NovaLauncher.App.Models;

public enum ApplicationSourceType { Executable, Shortcut }

// PNG bytes keep the business model independent of WPF and native handles.
public sealed record LauncherApplication(
    Guid Id, string DisplayName, string ExecutablePath, string SourcePath,
    byte[]? Icon, ApplicationSourceType SourceType)
{
    // Session-only in 4.2; model values can be persisted without UI types in 4.3.
    public long LaunchCount { get; init; }
    public DateTimeOffset? LastLaunchTime { get; init; }
}
