namespace NovaLauncher.App.ViewModels;

public static class LaunchTimeFormatter
{
    public static string Format(DateTimeOffset? timestamp, DateTimeOffset now)
    {
        if (timestamp is not { } value) return "从未";
        var local = value.ToLocalTime();
        var today = now.ToLocalTime();
        var elapsed = now - value;
        if (elapsed >= TimeSpan.Zero && elapsed < TimeSpan.FromMinutes(1)) return "刚刚";
        if (elapsed >= TimeSpan.FromMinutes(1) && elapsed < TimeSpan.FromHours(1)) return $"{(int)elapsed.TotalMinutes}分钟前";
        if (local.Date == today.Date) return $"今天 {local:HH:mm}";
        if (local.Date == today.Date.AddDays(-1)) return $"昨天 {local:HH:mm}";
        return local.Year == today.Year ? $"{local:M月d日}" : $"{local:yyyy年M月d日}";
    }
}
