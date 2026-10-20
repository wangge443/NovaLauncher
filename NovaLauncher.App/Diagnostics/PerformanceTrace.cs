using System.Diagnostics;

namespace NovaLauncher.App.Diagnostics;

/// <summary>Explicit diagnostic sessions only; normal startup does not collect samples.</summary>
public static class PerformanceTrace
{
    private static readonly Dictionary<string, double> Samples = [];
    public static bool Enabled { get; set; }
    public static NovaLog? Log { get; set; }
    public static long Start() => Enabled ? Stopwatch.GetTimestamp() : 0;
    public static void End(string name, long start)
    {
        if (!Enabled) return;
        double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        lock (Samples) Samples[name] = Samples.GetValueOrDefault(name) + milliseconds;
    }
    public static void Mark(string name)
    {
        if (!Enabled) return;
        using var process = Process.GetCurrentProcess();
        lock (Samples) Samples[name] = (DateTime.Now - process.StartTime).TotalMilliseconds;
    }
    public static Dictionary<string, double> Snapshot()
    {
        lock (Samples) return new(Samples);
    }
    public static void WriteSummary()
    {
        foreach (var entry in Snapshot())
            Log?.Write(LogLevel.Information, "PERF", FormattableString.Invariant($"{entry.Key} {entry.Value:F2} ms"));
    }
}
