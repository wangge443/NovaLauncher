using System.Diagnostics;
using System.IO;

namespace NovaLauncher.App.Services;

public interface IApplicationLocationService
{
    Task<string?> OpenAsync(string executablePath);
}

public sealed class ApplicationLocationService : IApplicationLocationService
{
    public Task<string?> OpenAsync(string executablePath) => Task.Run<string?>(() =>
    {
        try
        {
            if (!File.Exists(executablePath)) return "应用文件不存在";
            string path = Path.GetFullPath(executablePath);
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true
            });
            return null;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Open application location failed: {0}", ex);
            return "无法打开文件位置";
        }
    });
}
