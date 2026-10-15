using System.Diagnostics;
using System.IO;
using System.Reflection.PortableExecutable;
using NovaLauncher.App.Models;

namespace NovaLauncher.App.Services;

public sealed class ApplicationImportService(IShortcutResolver shortcuts, IApplicationIconService icons) : IApplicationImportService
{
    public Task<ApplicationImportResult> ImportAsync(string sourcePath, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<ApplicationImportResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Shell COM runs in its own STA; metadata and disk access never block the WPF dispatcher.
        var thread = new Thread(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = Import(sourcePath, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                completion.TrySetResult(result);
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(cancellationToken); }
            catch (Exception ex)
            {
                Trace.TraceWarning("Application import failed: {0}", ex.GetType().Name);
                completion.TrySetResult(new(null, "无法导入此文件，请检查文件或快捷方式目标"));
            }
        }) { IsBackground = true, Name = "Application import" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(cancellationToken);
    }

    private ApplicationImportResult Import(string sourcePath, CancellationToken token)
    {
        string extension = Path.GetExtension(sourcePath);
        bool isShortcut = extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase);
        if (!isShortcut && !extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            return new(null, "不支持此文件类型");

        string source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source)) return new(null, "文件不存在或无法访问");
        string target = isShortcut ? shortcuts.Resolve(source) : source;
        if (string.IsNullOrWhiteSpace(target) || !Path.IsPathFullyQualified(target))
            return new(null, "快捷方式已损坏或目标无效");
        target = Path.GetFullPath(target);
        if (!Path.GetExtension(target).Equals(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(target))
            return new(null, "目标程序不存在或不是有效的 .exe");

        // Reject renamed text files and DLLs without ever executing the target.
        using (var stream = File.OpenRead(target))
        using (var pe = new PEReader(stream))
        {
            var headers = pe.PEHeaders;
            if (headers.PEHeader is null ||
                !headers.CoffHeader.Characteristics.HasFlag(Characteristics.ExecutableImage) ||
                headers.CoffHeader.Characteristics.HasFlag(Characteristics.Dll))
                return new(null, "目标不是有效的可执行程序");
        }

        token.ThrowIfCancellationRequested();
        string name = Path.GetFileNameWithoutExtension(source);
        if (!isShortcut)
        {
            try
            {
                var version = FileVersionInfo.GetVersionInfo(target);
                string? product = ReliableName(version.ProductName);
                // Windows tools often share this OS-wide product label; the file description identifies the app.
                if (product is not null && product.Contains("Windows", StringComparison.OrdinalIgnoreCase)
                    && product.Contains("Operating System", StringComparison.OrdinalIgnoreCase)) product = null;
                name = product ?? ReliableName(version.FileDescription) ?? name;
            }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
            {
                Trace.TraceWarning("Metadata unavailable: {0}", ex.GetType().Name);
            }
        }
        byte[]? icon = icons.Extract(target);
        token.ThrowIfCancellationRequested();
        return new(new(Guid.NewGuid(), name, target, source, icon,
            isShortcut ? ApplicationSourceType.Shortcut : ApplicationSourceType.Executable), null);
    }

    private static string? ReliableName(string? value)
    {
        string? trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > 160 || trimmed.Any(char.IsControl) ? null : trimmed;
    }
}
