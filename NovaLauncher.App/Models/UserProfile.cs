namespace NovaLauncher.App.Models;

public sealed record UserProfile(int Id, string DisplayName, string? AvatarPath, DateTimeOffset UpdatedAt);
