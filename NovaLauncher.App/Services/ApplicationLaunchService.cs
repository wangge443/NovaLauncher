using System.Diagnostics;
using System.IO;
using NovaLauncher.App.Models;

namespace NovaLauncher.App.Services;

public sealed class ApplicationLaunchService : IApplicationLaunchService
{
    public Task<ApplicationLaunchResult> LaunchAsync(LauncherApplication application, CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<ApplicationLaunchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Shell execution may involve COM; run on a separate STA, never the UI dispatcher.
        var thread = new Thread(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool shortcut = application.SourceType == ApplicationSourceType.Shortcut;
                string path = shortcut ? application.SourcePath : application.ExecutablePath;
                if (!Path.IsPathFullyQualified(path) || !File.Exists(path) ||
                    !Path.GetExtension(path).Equals(shortcut ? ".lnk" : ".exe", StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(application.ExecutablePath))
                {
                    completion.TrySetResult(new(false, "无法启动该应用，文件或目标已不存在"));
                    return;
                }

                var startInfo = new ProcessStartInfo(path) { UseShellExecute = true };
                if (!shortcut) startInfo.WorkingDirectory = Path.GetDirectoryName(path)!;
                // For .lnk leave Arguments and WorkingDirectory to the shortcut/Shell.
                cancellationToken.ThrowIfCancellationRequested();
                using Process? process = Process.Start(startInfo);
                // A successful Shell request can reuse an existing process and return null.
                // Do not wait for exit or interpret process lifetime as launch success.
                completion.TrySetResult(new(true));
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(cancellationToken); }
            catch (Exception ex)
            {
                Trace.TraceWarning("Launch request failed ({0}, HRESULT {1:X8}): {2}",
                    ex.GetType().Name, ex.HResult, ex.Message);
                completion.TrySetResult(new(false, "无法启动该应用，请检查文件或访问权限"));
            }
        }) { IsBackground = true, Name = "Application launch" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        // Once Shell accepted a request, cancellation cannot undo it; preserve that result.
        return completion.Task;
    }
}
