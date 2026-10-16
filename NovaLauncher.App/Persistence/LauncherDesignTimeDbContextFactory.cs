using Microsoft.EntityFrameworkCore.Design;

namespace NovaLauncher.App.Persistence;

// Tooling creates a context only; it never initializes or upgrades the user's database.
public sealed class LauncherDesignTimeDbContextFactory : IDesignTimeDbContextFactory<LauncherDbContext>
{
    public LauncherDbContext CreateDbContext(string[] args) =>
        new LauncherDbContextFactory(ApplicationDataPaths.Default).CreateDbContext();
}
