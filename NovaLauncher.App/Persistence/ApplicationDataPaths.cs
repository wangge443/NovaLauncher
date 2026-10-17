using System.IO;

namespace NovaLauncher.App.Persistence;

public sealed class ApplicationDataPaths(string root)
{
    public string RootDirectory { get; } = Path.GetFullPath(root);
    public static ApplicationDataPaths Default => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaLauncher"));
    public string DatabasePath { get; } = Path.Combine(root, "Data", "nova.db");
    public string IconsDirectory { get; } = Path.Combine(root, "Cache", "Icons");
    public string BackgroundDirectory { get; } = Path.Combine(root, "Background");
    public string ProfileDirectory { get; } = Path.Combine(root, "Profile");
    public string BackupsDirectory { get; } = Path.Combine(root, "Data", "Backups");
    public string IconPath(Guid id) => Path.Combine(IconsDirectory, $"{id:N}.png");
}
