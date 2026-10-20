using System.Diagnostics;

namespace NovaLauncher.App.Diagnostics;

/// <summary>Bridges existing static Trace formats without serializing paths, arguments or exception messages.</summary>
public sealed class NovaTraceListener(NovaLog log) : TraceListener
{
    public override bool IsThreadSafe => true;
    public override void Write(string? message) { }
    public override void WriteLine(string? message) { }

    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
        => Record(eventType, message, null);

    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? format, params object?[]? args)
        => Record(eventType, format, args?.OfType<Exception>().FirstOrDefault());

    private void Record(TraceEventType type, string? format, Exception? exception)
    {
        var caller = new StackTrace(false).GetFrames().Select(frame => frame.GetMethod()?.DeclaringType)
            .FirstOrDefault(t => t?.Namespace?.StartsWith("NovaLauncher.App", StringComparison.Ordinal) == true
                && t.Namespace != typeof(NovaTraceListener).Namespace);
        string context = caller?.FullName ?? "Trace";
        // All current Trace calls use constant format strings. Arguments are intentionally omitted.
        string message = format ?? "Trace event";
        int placeholder = message.IndexOf('{');
        if (placeholder >= 0) message = message[..placeholder].TrimEnd(' ', ':', '(');
        log.Write(type is TraceEventType.Critical ? LogLevel.Critical : type is TraceEventType.Error ? LogLevel.Error
            : type is TraceEventType.Warning ? LogLevel.Warning : LogLevel.Information, context, message, exception);
    }
}
