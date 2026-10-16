using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NovaLauncher.App.Persistence;
using NovaLauncher.App.ViewModels;

namespace NovaLauncher.Verification;

internal static partial class Program
{
    private static SqliteConnection OpenDatabase(string path, bool readOnly = false)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private static string Query(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<object?[]>();
        while (reader.Read())
        {
            var values = new object[reader.FieldCount];
            reader.GetValues(values);
            rows.Add(values.Select(x => x is DBNull ? null : x).ToArray());
        }
        return JsonSerializer.Serialize(rows);
    }

    private static string Rows(string path)
    {
        using var connection = OpenDatabase(path, true);
        return Query(connection, "SELECT Id, DisplayName, ExecutablePath, TargetPathKey, SourcePath, SourceType, LaunchCount, LastLaunchTime, DateAdded, SortOrder, IconCachePath FROM Applications ORDER BY Id;");
    }

    private static int InspectDatabase()
    {
        using var connection = OpenDatabase(ApplicationDataPaths.Default.DatabasePath, true);
        Console.WriteLine(Query(connection, "SELECT type, name, sql FROM sqlite_schema ORDER BY type, name;"));
        Console.WriteLine(Query(connection, "SELECT DisplayName, LaunchCount, LastLaunchTime FROM Applications ORDER BY Id;"));
        return 0;
    }

    private static int BackupCount(ApplicationDataPaths paths) => Directory.Exists(paths.BackupsDirectory)
        ? Directory.GetFiles(paths.BackupsDirectory, "*.db").Length : 0;

    private static async Task SeedLegacyAsync(ApplicationDataPaths paths, bool withHistory = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(paths.DatabasePath)!);
        using var db = new LauncherDbContextFactory(paths).CreateDbContext();
        // Freeze the legacy fixture at 4.3, independent of future model changes.
        await db.Database.ExecuteSqlRawAsync(withHistory ? db.GetService<IMigrator>()
            .GenerateScript(Migration.InitialDatabase, DatabaseInitializationService.BaselineMigration) : LegacyCreateScript(db));
        for (int index = 0; index < 3; index++)
        {
            Guid id = Guid.NewGuid();
            string name = "Legacy 应用 " + index;
            string path = $@"C:\Legacy\{index}.exe";
            string key = path.ToUpperInvariant();
            DateTimeOffset? time = index == 0 ? null : new DateTimeOffset(2026, 10, 14, 12, 34, 56, TimeSpan.FromHours(-8)).AddTicks(index);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Applications (Id, DisplayName, ExecutablePath, SourcePath, TargetPathKey, SourceType, LaunchCount, LastLaunchTime, DateAdded, SortOrder) VALUES ({id}, {name}, {path}, {path}, {key}, 0, {index * 7}, {time}, {DateTimeOffset.UtcNow}, {index})");
        }
    }

    private static string LegacyCreateScript(LauncherDbContext db) => db.GetService<IMigrator>()
        .GenerateScript(Migration.InitialDatabase, DatabaseInitializationService.BaselineMigration) + "\nDROP TABLE __EFMigrationsHistory;";

    private static DatabaseInitializationService Initializer(ApplicationDataPaths paths) =>
        new(new LauncherDbContextFactory(paths), paths);

    private static async Task ExpectFailureAsync(DatabaseInitializationService service, string label)
    {
        try { await service.InitializeAsync(default); }
        catch (DatabaseInitializationException) { Check(true, label); return; }
        throw new InvalidOperationException("Expected initialization failure: " + label);
    }

    private static async Task<int> RunMigrationTestsAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "NovaLauncher-Migrations-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var fresh = new ApplicationDataPaths(Path.Combine(root, "fresh"));
            await Initializer(fresh).InitializeAsync(default);
            using (var db = new LauncherDbContextFactory(fresh).CreateDbContext())
            {
                Check((await db.Database.GetAppliedMigrationsAsync()).SequenceEqual(db.Database.GetMigrations()), "Fresh database applies migrations exactly once");
                Check(await db.Applications.CountAsync() == 0, "Fresh Applications exists and is empty");
                Check(!db.Database.HasPendingModelChanges(), "Snapshot matches current model");
            }
            Check(BackupCount(fresh) == 0, "Fresh database has no legacy backup");
            await Initializer(fresh).InitializeAsync(default);
            Check(BackupCount(fresh) == 0, "Migrated startup does not back up");

            var legacy = new ApplicationDataPaths(Path.Combine(root, "legacy"));
            await SeedLegacyAsync(legacy);
            string before = Rows(legacy.DatabasePath);
            // Keep WAL open to exercise online backup of committed WAL pages.
            using (var wal = OpenDatabase(legacy.DatabasePath))
            {
                using var command = wal.CreateCommand();
                command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; UPDATE Applications SET LaunchCount=LaunchCount+1;";
                command.ExecuteNonQuery();
                before = Rows(legacy.DatabasePath);
                await Initializer(legacy).InitializeAsync(default);
                Check(Rows(legacy.DatabasePath) == before, "Legacy preserves every column including count, null/time and offset");
                Check(BackupCount(legacy) == 1, "Legacy creates one backup");
                string backup = Directory.GetFiles(legacy.BackupsDirectory, "*.db").Single();
                Check(Rows(backup) == before, "Backup includes committed WAL data");
                using var backupConnection = OpenDatabase(backup, true);
                Check(Query(backupConnection, "SELECT name FROM sqlite_schema WHERE name='__EFMigrationsHistory';") == "[]", "Backup predates history bootstrap");
            }
            for (int index = 0; index < 3; index++) await Initializer(legacy).InitializeAsync(default);
            Check(BackupCount(legacy) == 1 && Rows(legacy.DatabasePath) == before, "Repeated independent initialization preserves data and single backup");
            using (var db = new LauncherDbContextFactory(legacy).CreateDbContext())
                Check((await db.Database.GetAppliedMigrationsAsync()).SequenceEqual(db.Database.GetMigrations()), "Legacy history contains baseline and subsequent migrations");

            var mismatch = new ApplicationDataPaths(Path.Combine(root, "mismatch"));
            await SeedLegacyAsync(mismatch);
            using (var connection = OpenDatabase(mismatch.DatabasePath))
            {
                using var command = connection.CreateCommand();
                command.CommandText = "DROP INDEX IX_Applications_TargetPathKey;";
                command.ExecuteNonQuery();
            }
            string mismatchBefore = Rows(mismatch.DatabasePath);
            await ExpectFailureAsync(Initializer(mismatch), "Missing unique index rejects bootstrap");
            Check(Rows(mismatch.DatabasePath) == mismatchBefore && BackupCount(mismatch) == 0, "Schema rejection leaves data intact and creates no backup");
            using (var connection = OpenDatabase(mismatch.DatabasePath, true))
                Check(Query(connection, "SELECT name FROM sqlite_schema WHERE name='__EFMigrationsHistory';") == "[]", "Rejected schema is not stamped");

            foreach (var (name, oldSql, newSql) in new[]
            {
                ("type", "\"DisplayName\" TEXT NOT NULL", "\"DisplayName\" TEXTNOTNULL"),
                ("check", "LaunchCount >= 0", "LaunchCount >= -1"),
                ("nullability", "\"DisplayName\" TEXT NOT NULL", "\"DisplayName\" TEXT NULL")
            })
            {
                var changed = new ApplicationDataPaths(Path.Combine(root, name));
                Directory.CreateDirectory(Path.GetDirectoryName(changed.DatabasePath)!);
                using (var db = new LauncherDbContextFactory(changed).CreateDbContext())
                {
                    string sql = LegacyCreateScript(db).Replace(oldSql, newSql, StringComparison.Ordinal);
                    await db.Database.ExecuteSqlRawAsync(sql);
                }
                await ExpectFailureAsync(Initializer(changed), "Schema mismatch rejected: " + name);
                Check(BackupCount(changed) == 0, "Rejected schema has no backup: " + name);
            }

            var concurrent = new ApplicationDataPaths(Path.Combine(root, "concurrent"));
            await SeedLegacyAsync(concurrent);
            using (var gate = new FileStream(concurrent.DatabasePath + ".initialization.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                await ExpectFailureAsync(Initializer(concurrent), "Concurrent initializer fails promptly without bootstrapping");
            Check(BackupCount(concurrent) == 0, "Concurrent initializer does not back up");
            await Initializer(concurrent).InitializeAsync(default);
            Check(BackupCount(concurrent) == 1, "Next startup succeeds after lock release");

            var blocked = new ApplicationDataPaths(Path.Combine(root, "blocked"));
            await SeedLegacyAsync(blocked);
            string blockedBefore = Rows(blocked.DatabasePath);
            File.WriteAllText(blocked.BackupsDirectory, "blocks backup directory");
            var failed = Initializer(blocked);
            await ExpectFailureAsync(failed, "Backup failure stops bootstrap");
            File.Delete(blocked.BackupsDirectory);
            await ExpectFailureAsync(failed, "Failed initialization is cached and not retried by imports");
            Check(Rows(blocked.DatabasePath) == blockedBefore && BackupCount(blocked) == 0, "Backup failure preserves original data");
            Check(File.ReadAllText(Path.Combine(Path.GetDirectoryName(blocked.DatabasePath)!, "migration-errors.log")).Contains("Exception"), "Detailed failure is recorded on disk");
            var error = await CreatePersistence(mismatch).LoadAsync(default);
            Check(error.Error == "数据库升级失败，现有数据未被删除", "Friendly startup failure hides exception stack");

            var conflict = new ApplicationDataPaths(Path.Combine(root, "history-conflict"));
            await SeedLegacyAsync(conflict);
            using (var connection = OpenDatabase(conflict.DatabasePath))
            {
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE __EFMigrationsHistory (MigrationId TEXT NOT NULL PRIMARY KEY, ProductVersion TEXT NOT NULL);";
                command.ExecuteNonQuery();
            }
            string conflictBefore = Rows(conflict.DatabasePath);
            await ExpectFailureAsync(Initializer(conflict), "Existing empty history goes through Migrate and fails safely");
            Check(Rows(conflict.DatabasePath) == conflictBefore && BackupCount(conflict) == 0, "Migration SQL failure preserves Applications and does not bootstrap");
            Console.WriteLine($"PASS: {_checks} migration assertions");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(root, true); }
    }

    private static async Task<int> VerifyRealMigrationAsync()
    {
        var paths = ApplicationDataPaths.Default;
        string before = Rows(paths.DatabasePath);
        int backups = BackupCount(paths);
        string snapshot = Path.Combine(Path.GetTempPath(), "NovaLauncher-real-migration-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(snapshot, before);
        try
        {
            InspectDatabase();
            for (int index = 0; index < 3; index++)
            {
                var start = new ProcessStartInfo(Environment.ProcessPath!)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                };
                start.ArgumentList.Add("--migration-window");
                start.ArgumentList.Add(snapshot);
                using var child = Process.Start(start)!;
                var stdout = child.StandardOutput.ReadToEndAsync();
                var stderr = child.StandardError.ReadToEndAsync();
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
                Check(child.ExitCode == 0, $"Real WPF startup {index + 1}: {await stdout} {await stderr}");
                Check(Rows(paths.DatabasePath) == before, "Real database all columns unchanged after process exit");
            }
            Check(BackupCount(paths) == backups + 1, "Real legacy upgrade created exactly one backup across three starts");
            Console.WriteLine("Backup: " + Directory.GetFiles(paths.BackupsDirectory, "*.db").Order().Last());
            InspectDatabase();
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { File.Delete(snapshot); }
    }

    private static int RunMigrationWindow(string snapshot)
    {
        var app = new NovaLauncher.App.App();
        app.InitializeComponent();
        app.Startup += (_, _) => app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                var vm = ((MainWindowViewModel)app.MainWindow.DataContext).Content;
                await vm.InitializeAsync();
                Check(vm.StatusMessage is null && vm.Applications.Count > 1, "Real application restores multiple cards");
                Check(Rows(ApplicationDataPaths.Default.DatabasePath) == File.ReadAllText(snapshot), "Original application rows preserved exactly");
                using var db = new LauncherDbContextFactory(ApplicationDataPaths.Default).CreateDbContext();
                var records = await db.Applications.AsNoTracking().ToListAsync();
                Check(records.Count == vm.Applications.Count && records.All(record => vm.Applications.Any(card =>
                    card.Application.Id == record.Id && card.DisplayName == record.DisplayName &&
                    card.LaunchCount == record.LaunchCount && card.LastLaunchTime == record.LastLaunchTime)), "Visible cards retain names, launch counts and exact timestamps");
                Check((await db.Database.GetAppliedMigrationsAsync()).SequenceEqual(db.Database.GetMigrations()), "Real database migration history correct");
                app.MainWindow.Close();
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); app.Shutdown(1); }
        }));
        return app.Run();
    }
}
