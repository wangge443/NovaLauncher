using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace NovaLauncher.App.Services;

/// <summary>One owner per Windows user/session. The mutex is owned and released on the UI thread.</summary>
public sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _listener;
    private bool _owned;
    private bool _disposed;

    public SingleInstanceCoordinator(string? verificationScope = null)
    {
        using var identity = WindowsIdentity.GetCurrent();
        using var process = Process.GetCurrentProcess();
        string scope = $"{identity.User?.Value ?? identity.Name}|{process.SessionId}|{verificationScope ?? "production"}";
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope)))[..32];
        _pipeName = "NovaLauncher-" + key;
        _mutex = new Mutex(false, "Local\\" + _pipeName);
    }

    public bool TryAcquire()
    {
        if (_owned) return true;
        try { _owned = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { _owned = true; }
        return _owned;
    }

    public void Listen(Action activationRequested)
    {
        if (!_owned) throw new InvalidOperationException("Only the primary instance can listen.");
        _listener ??= Task.Run(() => ListenAsync(activationRequested, _lifetime.Token));
    }

    private async Task ListenAsync(Action activationRequested, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 128, leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 128, leaveOpen: true) { AutoFlush = true };
                await writer.WriteLineAsync(Environment.ProcessId.ToString(CultureInfo.InvariantCulture).AsMemory(), timeout.Token).ConfigureAwait(false);
                // A fixed, bounded message is sufficient for activation; no arbitrary command execution.
                var message = new char[9];
                int count = await reader.ReadBlockAsync(message.AsMemory(), timeout.Token).ConfigureAwait(false);
                if (count == message.Length && new string(message) == "ACTIVATE\n")
                {
                    activationRequested();
                    await writer.WriteLineAsync("OK".AsMemory(), timeout.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                Trace.TraceWarning("Single-instance activation channel failed: {0}", ex);
                try { await Task.Delay(100, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    public async Task<bool> ActivateExistingAsync(CancellationToken token = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (!timeout.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.ConnectAsync(300, timeout.Token).ConfigureAwait(false);
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 128, leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 128, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
                string? pidText = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
                if (!int.TryParse(pidText, CultureInfo.InvariantCulture, out int pid) || pid <= 0) return false;
                // Windows grants the foreground launcher's permission to the existing window process.
                AllowSetForegroundWindow((uint)pid);
                await writer.WriteLineAsync("ACTIVATE".AsMemory(), timeout.Token).ConfigureAwait(false);
                return await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false) == "OK";
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested) { return false; }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
            {
                try { await Task.Delay(100, timeout.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return false; }
            }
        }
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        if (_owned) { _mutex.ReleaseMutex(); _owned = false; }
        _mutex.Dispose();
        // WaitForConnectionAsync is cancellable. Observe completion without blocking the Dispatcher.
        _ = _listener?.ContinueWith(_ => _lifetime.Dispose(), TaskScheduler.Default);
        if (_listener is null) _lifetime.Dispose();
    }

    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint processId);
}
