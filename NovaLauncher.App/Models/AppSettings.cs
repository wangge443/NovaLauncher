namespace NovaLauncher.App.Models;

public enum StartupPage { Home, Recent, Favorites }
public enum BackgroundMode { Default }
public enum ThemeMode { Light }

public record AppSettings
{
    public int Id { get; init; } = 1;
    public StartupPage StartupPage { get; init; } = StartupPage.Home;
    public bool RememberWindowSize { get; init; } = true;
    public bool RememberWindowPosition { get; init; } = true;
    public double? WindowWidth { get; init; }
    public double? WindowHeight { get; init; }
    public double? WindowLeft { get; init; }
    public double? WindowTop { get; init; }
    public BackgroundMode BackgroundMode { get; init; } = BackgroundMode.Default;
    public ThemeMode ThemeMode { get; init; } = ThemeMode.Light;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}
