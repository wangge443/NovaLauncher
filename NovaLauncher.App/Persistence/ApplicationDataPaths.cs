using System.IO;

namespace NovaLauncher.App.Persistence;

public sealed class ApplicationDataPaths(string root)
{
    public string RootDirectory { get; } = Path.GetFullPath(root);
    private static string ProductionRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaLauncher");
    public static string? VerificationRoot { get; private set; }
    public static ApplicationDataPaths Default => new(VerificationRoot ?? ProductionRoot);
    // Internal publish verification only. A pre-created marker prevents accidental user-data selection.
    public static void ConfigureVerificationRoot(IEnumerable<string> arguments)
    {
        const string prefix = "--verification-data-root=";
        var values = arguments.Where(x => x.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        if (values.Length == 0) return;
        if (values.Length != 1) throw new ArgumentException("Specify one verification data root.");
        string root = values[0][prefix.Length..];
        if (!Path.IsPathFullyQualified(root)) throw new ArgumentException("Verification root must be absolute.");
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string production = Path.GetFullPath(ProductionRoot);
        string rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (string.Equals(root, production, StringComparison.OrdinalIgnoreCase) ||
            production.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) ||
            root.StartsWith(production + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(Path.Combine(root, ".nova-verification")))
            throw new ArgumentException("Verification root must be isolated and contain .nova-verification.");
        VerificationRoot = root;
    }
    public string DatabasePath { get; } = Path.Combine(root, "Data", "nova.db");
    public string IconsDirectory { get; } = Path.Combine(root, "Cache", "Icons");
    public string CustomIconsDirectory { get; } = Path.Combine(root, "CustomIcons");
    public string BackgroundDirectory { get; } = Path.Combine(root, "Background");
    public string ProfileDirectory { get; } = Path.Combine(root, "Profile");
    public string BackupsDirectory { get; } = Path.Combine(root, "Data", "Backups");
    public string IconPath(Guid id) => Path.Combine(IconsDirectory, $"{id:N}.png");
}
