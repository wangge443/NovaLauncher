using System.Diagnostics;
using System.IO;
using System.Text;
using NovaLauncher.App.Diagnostics;

namespace NovaLauncher.Verification;

internal static partial class Program
{
    private static async Task<int> RunLoggingTestsAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "NovaLauncher-Logging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        int checks = 0;
        void CheckLog(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("Logging verification failed: " + name);
            checks++; Console.WriteLine("PASS " + name);
        }
        try
        {
            string basic = Path.Combine(root, "basic");
            var log = new NovaLog(basic);
            log.WriteStartup("0.1.0-preview.1");
            Exception failure;
            try { throw new IOException("secret-user C:\\private\\avatar.png --password=hunter2"); }
            catch (IOException ex) { failure = ex; }
            log.Write(LogLevel.Error, "Verification", "测试写入", failure);
            using (var listener = new NovaTraceListener(log))
            {
                listener.TraceEvent(null, "Test", TraceEventType.Error, 0,
                    "Persistence failed: {0} {1}", failure, "sensitive-user-content");
            }
            log.Write(LogLevel.Information, "Application", "Shutdown");
            await log.StopAsync();
            string contents = await File.ReadAllTextAsync(Directory.GetFiles(basic).Single(), new UTF8Encoding(false, true));
            CheckLog(contents.Contains("测试写入") && contents.Contains("[ERROR]"), "UTF-8, level and event");
            CheckLog(contents.Contains("Start version=0.1.0-preview.1") && contents.Contains("Shutdown"), "startup version and shutdown drained");
            CheckLog(DateTimeOffset.TryParse(contents.Split(' ')[0], out _), "UTC timestamp");
            CheckLog(contents.Contains("System.IO.IOException") && contents.Contains("HResult=0x"), "safe exception diagnostics");
            CheckLog(!contents.Contains("secret-user") && !contents.Contains("hunter2") && !contents.Contains("sensitive-user-content")
                && !contents.Contains("avatar.png"), "exception messages and Trace arguments excluded");

            string rotating = Path.Combine(root, "rotation");
            var rotation = new NovaLog(rotating, new(512, 1024, 3));
            for (int i = 0; i < 100; i++) rotation.Write(LogLevel.Information, "Rotation", new string('x', 200));
            await rotation.StopAsync();
            var retained = Directory.GetFiles(rotating);
            CheckLog(retained.Length == 3 && retained.All(f => new FileInfo(f).Length <= 1024), "file size and total retention bound");
            var restarted = new NovaLog(rotating, new(512, 1024, 3));
            restarted.Write(LogLevel.Information, "Rotation", "new process generation");
            await restarted.StopAsync();
            CheckLog(Directory.GetFiles(rotating).Length == 3, "retention spans logger restarts");

            string blocked = Path.Combine(root, "blocked");
            await File.WriteAllTextAsync(blocked, "not a directory");
            var unavailable = new NovaLog(blocked);
            unavailable.Write(LogLevel.Error, "Failure", "unwritable output");
            await unavailable.StopAsync();
            CheckLog(unavailable.WriteFailures == 1, "write failure cannot escape into caller");

            var bounded = new NovaLog(Path.Combine(root, "bounded"), new(1, 1024, 2));
            Parallel.For(0, 10000, _ => bounded.Write(LogLevel.Information, "Pressure", "queue saturation"));
            await bounded.StopAsync();
            CheckLog(bounded.DroppedEntries > 0, "bounded queue drops instead of blocking producers");
            var emergency = new NovaLog(Path.Combine(root, "emergency"));
            emergency.Write(LogLevel.Critical, "Fatal", "fatal exception", failure);
            CheckLog(emergency.FlushEmergency(TimeSpan.FromSeconds(1)), "bounded emergency flush independent of dispatcher");
            CheckLog(Directory.GetFiles(Path.Combine(root, "emergency")).Length == 1, "fatal event persisted");
            Console.WriteLine($"PASS: {checks} logging assertions");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(root, true); }
    }
}
