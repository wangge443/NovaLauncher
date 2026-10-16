namespace NovaLauncher.App.Persistence;

public sealed class ApplicationEntity
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public string TargetPathKey { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public int SourceType { get; set; }
    public long LaunchCount { get; set; }
    public DateTimeOffset? LastLaunchTime { get; set; }
    public DateTimeOffset DateAdded { get; set; }
    public long SortOrder { get; set; }
    public string? IconCachePath { get; set; }
}
