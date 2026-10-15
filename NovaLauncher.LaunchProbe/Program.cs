using System.Text.Json;

// Test-only child process; never referenced by the production application.
string? output = Environment.GetEnvironmentVariable("NOVA_LAUNCH_TEST_OUTPUT");
if (string.IsNullOrWhiteSpace(output) || !Directory.Exists(output)) return;
string file = Path.Combine(output, Guid.NewGuid().ToString("N"));
File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(new
{
    Arguments = args,
    WorkingDirectory = Environment.CurrentDirectory,
    ExecutablePath = Environment.ProcessPath,
    ProcessId = Environment.ProcessId
}));
File.Move(file + ".tmp", file + ".json");
