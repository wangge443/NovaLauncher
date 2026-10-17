using System.Diagnostics;
using System.IO;
using NovaLauncher.App.Models;
using NovaLauncher.App.Persistence;

namespace NovaLauncher.App.Services;

public interface IAppSettingsService
{
    AppSettings Current { get; }
    Task<bool> LoadAsync(CancellationToken token = default);
    Task<bool> UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken token = default);
}

public sealed class AppSettingsService(IAppSettingsRepository repository) : IAppSettingsService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public AppSettings Current { get; private set; } = new();

    public async Task<bool> LoadAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try { Current = Validate(await repository.GetOrCreateAsync(token)); return true; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { Trace.TraceError("Settings load failed: {0}", ex); Current = new(); return false; }
        finally { _gate.Release(); }
    }

    public async Task<bool> UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var next = Validate(update(Current)) with { UpdatedAt = DateTimeOffset.UtcNow };
            await repository.SaveAsync(next, token);
            Current = next;
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { Trace.TraceError("Settings save failed: {0}", ex); return false; }
        finally { _gate.Release(); }
    }

    public static AppSettings Validate(AppSettings value) => value with
    {
        Id = 1,
        StartupPage = Enum.IsDefined(value.StartupPage) ? value.StartupPage : StartupPage.Home,
        BackgroundMode = BackgroundMode.Default, ThemeMode = ThemeMode.Light,
        WindowWidth = Dimension(value.WindowWidth, 800, 1120),
        WindowHeight = Dimension(value.WindowHeight, 560, 720),
        WindowLeft = Coordinate(value.WindowLeft), WindowTop = Coordinate(value.WindowTop)
    };
    private static double? Dimension(double? value, double minimum, double fallback) => value is null ? null :
        double.IsFinite(value.Value) ? Math.Clamp(value.Value, minimum, 7680) : fallback;
    private static double? Coordinate(double? value) => value is { } number && double.IsFinite(number) && Math.Abs(number) <= 100000 ? number : null;
}

public interface IDataDirectoryService { Task OpenAsync(); }
public sealed class DataDirectoryService(ApplicationDataPaths paths) : IDataDirectoryService
{
    public Task OpenAsync() => Task.Run(() =>
    {
        Directory.CreateDirectory(paths.RootDirectory);
        using var process = Process.Start(new ProcessStartInfo(paths.RootDirectory) { UseShellExecute = true });
    });
}
