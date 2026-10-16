using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace NovaLauncher.App.Persistence;

public sealed class DatabaseInitializationException(Exception inner)
    : Exception("数据库升级失败，现有数据未被删除", inner);

public sealed class DatabaseInitializationService(
    IDbContextFactory<LauncherDbContext> factory, ApplicationDataPaths paths)
{
    // This ID must remain tied to the immutable initial migration, never the latest model.
    public const string BaselineMigration = "20261016040336_InitialApplications";
    private readonly object _sync = new();
    private Task? _initialization;

    public Task InitializeAsync(CancellationToken token)
    {
        lock (_sync)
        {
            // Cache failures too: imports must not repeatedly retry a failed upgrade.
            _initialization ??= Task.Run(InitializeCoreAsync);
            return _initialization.WaitAsync(token);
        }
    }

    private async Task InitializeCoreAsync()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(paths.DatabasePath)!);
            // Serialize the whole bootstrap + Migrate across launcher processes. Fail promptly if busy.
            using var gate = new FileStream(paths.DatabasePath + ".initialization.lock",
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            using var db = factory.CreateDbContext();
            await db.Database.OpenConnectionAsync();
            var connection = (SqliteConnection)db.Database.GetDbConnection();
            using (var transaction = connection.BeginTransaction(deferred: false))
            {
                var schema = ReadSchema(connection, transaction);
                if (!schema.Any(x => x.Name == "__EFMigrationsHistory"))
                {
                    if (schema.Count > 0)
                    {
                        ValidateLegacy(db, schema);
                        Backup();
                        // Only legacy bootstrap writes history directly. DDL and row commit atomically.
                        using var command = connection.CreateCommand();
                        command.Transaction = transaction;
                        command.CommandText = db.GetService<IHistoryRepository>().GetCreateScript();
                        command.ExecuteNonQuery();
                        command.CommandText = "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ($id, $version);";
                        command.Parameters.AddWithValue("$id", BaselineMigration);
                        command.Parameters.AddWithValue("$version", "9.0.16");
                        command.ExecuteNonQuery();
                    }
                }
                transaction.Commit();
            }
            // EF's SQLite migration lock can survive a killed process. Do not wait indefinitely.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await db.Database.MigrateAsync(timeout.Token);
        }
        catch (Exception ex)
        {
            Trace.TraceError("Database initialization failed: {0}", ex);
            try
            {
                File.AppendAllText(Path.Combine(Path.GetDirectoryName(paths.DatabasePath)!, "migration-errors.log"),
                    $"{DateTimeOffset.UtcNow:O} {ex}{Environment.NewLine}");
            }
            catch (Exception logError) { Trace.TraceError("Cannot write migration log: {0}", logError); }
            throw new DatabaseInitializationException(ex);
        }
    }

    private static void ValidateLegacy(LauncherDbContext db, List<SchemaEntry> actual)
    {
        // Build the reference from the frozen migration, not EnsureCreated or the evolving live model.
        using var reference = new SqliteConnection("Data Source=:memory:");
        reference.Open();
        using var command = reference.CreateCommand();
        command.CommandText = db.GetService<IMigrator>().GenerateScript(Migration.InitialDatabase, BaselineMigration);
        command.ExecuteNonQuery();
        var expected = ReadSchema(reference, null).Where(x => x.Name != "__EFMigrationsHistory").ToList();
        if (!actual.SequenceEqual(expected))
            throw new InvalidDataException("Legacy schema does not match InitialApplications; automatic bootstrap refused.");
    }

    private static List<SchemaEntry> ReadSchema(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT type, name, sql FROM sqlite_schema WHERE name NOT LIKE 'sqlite_%' ORDER BY type, name;";
        using var reader = command.ExecuteReader();
        var result = new List<SchemaEntry>();
        while (reader.Read())
        {
            string sql = reader.IsDBNull(2) ? "" : reader.GetString(2);
            // Preserve quoted tokens, including literal whitespace; ignore formatting outside tokens.
            string normalized = string.Join(" ", Regex.Matches(sql, "'([^']|'')*'|\"([^\"]|\"\")*\"|[A-Za-z_][A-Za-z0-9_]*|[0-9]+|\\S")
                .Select(match => match.Value));
            result.Add(new(reader.GetString(0), reader.GetString(1), normalized));
        }
        return result;
    }

    private void Backup()
    {
        Directory.CreateDirectory(paths.BackupsDirectory);
        string backupPath = Path.Combine(paths.BackupsDirectory,
            $"nova-before-migrations-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffffffZ}-{Guid.NewGuid():N}.db");
        // Reserve without overwriting. A separate reader sees the committed WAL snapshot while the
        // bootstrap connection holds a write reservation, preventing other writers from changing it.
        using (new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false
        }.ToString());
        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = backupPath, Mode = SqliteOpenMode.ReadWrite, Pooling = false
        }.ToString());
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
        using var check = destination.CreateCommand();
        check.CommandText = "PRAGMA integrity_check;";
        if (!Equals(check.ExecuteScalar(), "ok")) throw new InvalidDataException("Backup integrity check failed.");
    }

    private sealed record SchemaEntry(string Type, string Name, string Sql);
}
