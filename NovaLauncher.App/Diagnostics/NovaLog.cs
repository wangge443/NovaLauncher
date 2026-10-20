using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Channels;

namespace NovaLauncher.App.Diagnostics;

public enum LogLevel { Information, Warning, Error, Critical }

public sealed record LogOptions(int Capacity = 512, int MaxFileBytes = 1024 * 1024, int MaxFiles = 10);

/// <summary>Bounded, best-effort diagnostics. Callers supply static event names, never user data.</summary>
public sealed class NovaLog : IAsyncDisposable
{
    private readonly Channel<string> _queue;
    private readonly Task _writer;
    private readonly string _directory;
    private readonly LogOptions _options;
    private long _dropped;
    private long _failures;
    public long DroppedEntries => Interlocked.Read(ref _dropped);
    public long WriteFailures => Interlocked.Read(ref _failures);

    public NovaLog(string directory, LogOptions? options = null)
    {
        _directory = directory;
        _options = options ?? new();
        if (_options.Capacity < 1 || _options.MaxFileBytes < 1024 || _options.MaxFiles < 1)
            throw new ArgumentOutOfRangeException(nameof(options));
        _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(_options.Capacity)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        _writer = Task.Run(WriteLoopAsync);
    }

    public void Write(LogLevel level, string context, string eventName, Exception? exception = null)
    {
        // No file I/O, waits or exception.Message/ToString on the calling (possibly UI) thread.
        string line = $"{DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)} [{level.ToString().ToUpperInvariant()}] {Clean(context, 160)} {Clean(eventName, 400)}";
        if (exception is not null) line += " " + DescribeException(exception);
        if (!_queue.Writer.TryWrite(line)) Interlocked.Increment(ref _dropped);
    }

    public void WriteStartup(string version)
    {
        // Version is assembly metadata; constrain it rather than accepting arbitrary startup text.
        string safeVersion = new(version.Where(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+').Take(100).ToArray());
        Write(LogLevel.Information, "Application", "Start version=" + safeVersion + " runtime=" + Environment.Version);
    }

    public Task StopAsync()
    {
        _queue.Writer.TryComplete();
        return _writer;
    }

    public bool FlushEmergency(TimeSpan timeout)
    {
        // Only used on fatal paths. Writer is independent of the dispatcher and cannot deadlock it.
        try { return StopAsync().Wait(timeout); }
        catch { return false; }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task WriteLoopAsync()
    {
        FileStream? stream = null;
        long size = 0;
        try
        {
            await foreach (string line in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    int maxCharacters = (_options.MaxFileBytes - 8) / 4;
                    byte[] bytes = Encoding.UTF8.GetBytes((line.Length > maxCharacters ? line[..maxCharacters] : line) + Environment.NewLine);
                    if (stream is null || size + bytes.Length > _options.MaxFileBytes)
                    {
                        if (stream is not null) await stream.DisposeAsync().ConfigureAwait(false);
                        stream = null;
                        Directory.CreateDirectory(_directory);
                        Prune();
                        string name = "nova-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".log";
                        stream = new FileStream(Path.Combine(_directory, name), FileMode.CreateNew, FileAccess.Write,
                            FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous);
                        size = 0;
                    }
                    await stream.WriteAsync(bytes).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                    size += bytes.Length;
                }
                catch (Exception)
                {
                    Interlocked.Increment(ref _failures);
                    if (stream is not null)
                    {
                        try { await stream.DisposeAsync().ConfigureAwait(false); } catch { }
                        stream = null;
                    }
                    // Do not Trace here: the listener writes into this same queue.
                }
            }
        }
        finally
        {
            if (stream is not null)
                try { await stream.DisposeAsync().ConfigureAwait(false); } catch { Interlocked.Increment(ref _failures); }
        }
    }

    private void Prune()
    {
        var files = Directory.EnumerateFiles(_directory, "nova-*.log").OrderBy(x => x, StringComparer.Ordinal).ToList();
        // Abort opening another file if deletion fails, keeping the retention bound intact.
        foreach (string file in files.Take(Math.Max(0, files.Count - _options.MaxFiles + 1))) File.Delete(file);
    }

    private static string Clean(string value, int limit) => new(value.Where(c => !char.IsControl(c)).Take(limit).ToArray());

    internal static string DescribeException(Exception exception)
    {
        var parts = new List<string>();
        for (Exception? current = exception; current is not null && parts.Count < 3; current = current.InnerException)
        {
            string methods = string.Join(" <- ", new StackTrace(current, false).GetFrames().Take(6)
                .Select(frame => frame.GetMethod()).Where(method => method is not null)
                .Select(method => method!.DeclaringType?.FullName + "." + method.Name));
            string providerCode = current is Microsoft.Data.Sqlite.SqliteException sqlite
                ? $" SQLite={sqlite.SqliteErrorCode}/{sqlite.SqliteExtendedErrorCode}" : "";
            parts.Add($"{current.GetType().FullName} HResult=0x{current.HResult:X8}{providerCode} [{methods}]");
        }
        return Clean(string.Join(" Inner=", parts), 1200);
    }
}
