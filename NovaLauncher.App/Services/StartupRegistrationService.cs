using System.IO;
using System.Security;
using Microsoft.Win32;
using NovaLauncher.App.Diagnostics;

namespace NovaLauncher.App.Services;

public interface IStartupRegistrationStore
{
    string? Read();
    void Write(string command);
    void Delete();
}

public sealed class RegistryStartupStore(string keyPath = RegistryStartupStore.RunKey) : IStartupRegistrationStore
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "NovaLauncher";
    public string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }
    public void Write(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
        key.SetValue(ValueName, command, RegistryValueKind.String);
    }
    public void Delete()
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

public sealed record StartupRegistrationState(bool Enabled, string? Error = null);
public interface IStartupRegistrationService
{
    StartupRegistrationState Read();
    StartupRegistrationState SetEnabled(bool enabled);
    StartupRegistrationState RepairMovedPath();
}

// Registry is the only source of truth; removing its value must never recreate an opt-in.
public sealed class StartupRegistrationService(IStartupRegistrationStore store, string executable, NovaLog? log = null) : IStartupRegistrationService
{
    public static string CommandFor(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.Contains('"') ||
            !string.Equals(Path.GetFileName(path), "NovaLauncher.exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A full NovaLauncher.exe path is required.", nameof(path));
        string command = $"\"{Path.GetFullPath(path)}\" --startup";
        if (command.Length > 260) throw new ArgumentException("Run command exceeds Windows limit.", nameof(path));
        return command;
    }
    public StartupRegistrationState Read()
    {
        try { return new(string.Equals(store.Read(), CommandFor(executable), StringComparison.OrdinalIgnoreCase)); }
        catch (Exception ex) when (Expected(ex)) { return Failure(ex); }
    }
    public StartupRegistrationState SetEnabled(bool enabled)
    {
        try
        {
            if (enabled) store.Write(CommandFor(executable)); else store.Delete();
            var actual = Read();
            if (actual.Error is not null || actual.Enabled != enabled)
                return actual with { Error = actual.Error ?? "开机启动设置未生效，请重试。" };
            log?.Write(LogLevel.Information, "Startup", enabled ? "Startup registration enabled" : "Startup registration disabled");
            return actual;
        }
        catch (Exception ex) when (Expected(ex))
        {
            var failure = Failure(ex);
            return failure with { Enabled = Read().Enabled };
        }
    }
    public StartupRegistrationState RepairMovedPath()
    {
        try
        {
            string? old = store.Read();
            // Repair only a recognizable previous Nova opt-in. Missing/foreign values stay untouched.
            if (old is not null && old.StartsWith('"') && old.EndsWith("\" --startup", StringComparison.OrdinalIgnoreCase))
            {
                string path = old[1..^11];
                if (Path.IsPathFullyQualified(path) && Path.GetFileName(path).Equals("NovaLauncher.exe", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(old, CommandFor(executable), StringComparison.OrdinalIgnoreCase))
                {
                    var state = SetEnabled(true);
                    if (state.Enabled && state.Error is null) log?.Write(LogLevel.Information, "Startup", "Startup path repaired");
                    return state;
                }
            }
            return Read();
        }
        catch (Exception ex) when (Expected(ex)) { return Failure(ex); }
    }
    private static bool Expected(Exception ex) => ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException;
    private StartupRegistrationState Failure(Exception ex)
    {
        log?.Write(LogLevel.Warning, "Startup", "Startup registration failure", ex);
        return new(false, "无法更改或读取 Windows 开机启动设置，请检查当前用户权限后重试。");
    }
}
