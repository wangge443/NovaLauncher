using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

namespace NovaLauncher.App.Persistence;

public sealed class LauncherDbContext(DbContextOptions<LauncherDbContext> options) : DbContext(options)
{
    public DbSet<ApplicationEntity> Applications => Set<ApplicationEntity>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        var app = builder.Entity<ApplicationEntity>();
        app.HasKey(x => x.Id);
        app.HasIndex(x => x.TargetPathKey).IsUnique();
        app.Property(x => x.DisplayName).IsRequired();
        app.Property(x => x.ExecutablePath).IsRequired();
        app.Property(x => x.SourcePath).IsRequired();
        app.Property(x => x.TargetPathKey).IsRequired();
        app.ToTable("Applications", table =>
        {
            table.HasCheckConstraint("CK_Application_Paths", "length(trim(ExecutablePath)) > 0 AND length(trim(SourcePath)) > 0 AND length(trim(TargetPathKey)) > 0");
            table.HasCheckConstraint("CK_Application_Values", "LaunchCount >= 0 AND SourceType IN (0, 1)");
        });
    }
}

public sealed class LauncherDbContextFactory(ApplicationDataPaths paths) : IDbContextFactory<LauncherDbContext>
{
    public LauncherDbContext CreateDbContext() => new(new DbContextOptionsBuilder<LauncherDbContext>()
        .UseSqlite(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath, Pooling = false, DefaultTimeout = 5
        }.ToString()).Options);
}
