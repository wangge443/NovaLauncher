namespace NovaLauncher.App.Models;

// Home is the stable persisted value for the All Applications startup destination.
public enum StartupPage { Home, Recent, Favorites }
public enum BackgroundMode { Default, DesktopWallpaper, CustomImage, Transparent }
public enum BackgroundStretch { UniformToFill, Uniform }
public enum ThemeMode { Light }

public record AppSettings
{
    public int Id { get; init; } = 1;
    public bool CloseToTray { get; init; } = true;
    public bool GlobalHotkeyEnabled { get; init; } = true;
    public int GlobalHotkeyModifiers { get; init; } = 3;
    public int GlobalHotkeyKey { get; init; } = 32;
    public StartupPage StartupPage { get; init; } = StartupPage.Home;
    public bool RememberWindowSize { get; init; } = true;
    public bool RememberWindowPosition { get; init; } = true;
    public double? WindowWidth { get; init; }
    public double? WindowHeight { get; init; }
    public double? WindowLeft { get; init; }
    public double? WindowTop { get; init; }
    public BackgroundMode BackgroundMode { get; init; } = BackgroundMode.Default;
    public string? BackgroundImagePath { get; init; }
    public double BackgroundOverlayOpacity { get; init; } = 0.72;
    public double BackgroundBlurRadius { get; init; } = 12;
    public BackgroundStretch BackgroundStretch { get; init; } = BackgroundStretch.UniformToFill;
    public ThemeMode ThemeMode { get; init; } = ThemeMode.Light;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}
