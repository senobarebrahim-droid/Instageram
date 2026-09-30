using System.IO;
using Microsoft.Data.Sqlite;

namespace Instageram;

/// <summary>
/// Phase 4: keeps a damaged database from ending the session.
///
/// Order of operations when the database is unusable:
///   1. the broken file is moved aside (never deleted) into data\corrupt\
///   2. a fresh database is created with the current schema
///   3. every table that can still be read is copied over, column by column,
///      so an older schema does not block the salvage
///   4. whatever could not be recovered is reported, and the user's own backup
///      packages remain available for a full restore
/// </summary>
public static class DatabaseHealth
{
    public sealed class RecoveryResult
    {
        public bool Recovered { get; init; }
        public bool Quarantined { get; init; }
        public string? QuarantinePath { get; init; }
        public int SalvagedTables { get; init; }
        public long SalvagedRows { get; init; }
        public string Message { get; init; } = "";
    }

    /// <summary>The tables worth salvaging, parents before children.</summary>
    private static readonly string[] SalvageOrder =
    {
        "projects",
        "campaigns",
        "countries",
        "campaign_countries",
        "campaign_statistics",
        "profile_statistics",
        "reports",
        "app_settings",
        "logs"
    };

    public static bool LooksLikeSqlite(string path)
    {
        try
        {
            using var stream = OpenForPeek(path);

            var header = new byte[16];

            if (stream.Read(header, 0, header.Length) != header.Length)
                return false;

            return System.Text.Encoding.ASCII.GetString(header) == "SQLite format 3\0";
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Opens a file for reading its header without fighting whoever else has it
    /// open. A plain File.OpenRead only shares Read, so it fails with "being
    /// used by another process" as soon as a SQLite connection holds the file â€”
    /// which would make a perfectly healthy database look unreadable.
    /// </summary>
    private static FileStream OpenForPeek(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    /// <summary>
    /// First bytes of a file as hex, for the diagnostics report. Makes a
    /// "not a database" verdict verifiable instead of a guess.
    /// </summary>
    public static string HeaderHex(string path, int count = 16)
    {
        try
        {
            using var stream = OpenForPeek(path);

            var buffer = new byte[count];
            var read = stream.Read(buffer, 0, count);

            return Convert.ToHexString(buffer, 0, read);
        }
        catch (Exception ex)
        {
            return "error: " + ex.Message;
        }
    }

    /// <summary>What a database file looks like right now.</summary>
    public enum DatabaseState
    {
        /// <summary>No file yet: a new installation.</summary>
        Missing,

        /// <summary>The file exists but could not be read (locked, permissions). NOT corruption.</summary>
        Unreadable,

        /// <summary>The file is readable but is not a SQLite database.</summary>
        NotSqlite,

        /// <summary>SQLite opened it but its own integrity check failed.</summary>
        Corrupt,

        /// <summary>Readable and consistent.</summary>
        Healthy
    }

    /// <summary>
    /// Inspects a database file without ever guessing. A file that cannot be
    /// read is reported as Unreadable and is deliberately NOT treated as
    /// corruption: another running copy of the application may hold it, and
    /// quarantining a healthy database would destroy user data.
    /// </summary>
    public static DatabaseState Inspect(string path, out string problem)
    {
        problem = "";

        if (!File.Exists(path))
        {
            problem = "the file does not exist";
            return DatabaseState.Missing;
        }

        string header;

        try
        {
            using var stream = OpenForPeek(path);

            var buffer = new byte[16];
            var read = stream.Read(buffer, 0, buffer.Length);

            if (read != buffer.Length)
            {
                problem = "the file is too short to be a database";
                return DatabaseState.NotSqlite;
            }

            header = System.Text.Encoding.ASCII.GetString(buffer);
        }
        catch (Exception ex)
        {
            problem = "the file could not be read: " + ex.Message;
            return DatabaseState.Unreadable;
        }

        if (header != "SQLite format 3\0")
        {
            problem = "the file is not a SQLite database";
            return DatabaseState.NotSqlite;
        }

        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());

            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";

            var result = command.ExecuteScalar()?.ToString() ?? "";

            if (string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                return DatabaseState.Healthy;

            problem = result;
            return DatabaseState.Corrupt;
        }
        catch (Exception ex)
        {
            problem = ex.Message;

            // A lock is not corruption. Anything else means SQLite cannot use
            // the file, which is a real problem but still must not be judged
            // as damage unless the caller decides to.
            return ex.Message.Contains("locked", StringComparison.OrdinalIgnoreCase) ||
                   ex.Message.Contains("busy", StringComparison.OrdinalIgnoreCase)
                ? DatabaseState.Unreadable
                : DatabaseState.Corrupt;
        }
    }

    /// <summary>Runs SQLite's own integrity check. Never throws.</summary>
    public static bool IsHealthy(string path, out string problem) =>
        Inspect(path, out problem) == DatabaseState.Healthy;

    /// <summary>
    /// Makes sure a usable database exists at the given path. Returns a result
    /// describing what had to be done; a healthy or brand new database gives an
    /// empty result.
    /// </summary>
    public static RecoveryResult EnsureUsable(string databasePath)
    {
        var state = Inspect(databasePath, out var problem);

        switch (state)
        {
            case DatabaseState.Missing:
                return new RecoveryResult { Message = "no database yet; a new one will be created" };

            case DatabaseState.Healthy:
                return new RecoveryResult { Message = "database is healthy" };

            case DatabaseState.Unreadable:
                // Deliberately untouched: a locked file is usually a second
                // running copy, and the data may be perfectly fine.
                AppLogger.Error("Recovery", "The database is not readable and was left untouched: " + problem);

                return new RecoveryResult { Message = "database is not readable: " + problem };

            default:
                AppLogger.Error("Recovery", $"The database is unusable ({state}): {problem}. Starting recovery.");
                break;
        }

        var quarantine = MoveToQuarantine(databasePath);

        SqliteConnection.ClearAllPools();

        var rows = 0L;
        var tables = 0;

        if (quarantine != null)
        {
            try
            {
                (tables, rows) = Salvage(quarantine, databasePath);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Recovery", "Salvage failed: " + ex);
            }
        }

        return new RecoveryResult
        {
            Recovered = true,
            Quarantined = quarantine != null,
            QuarantinePath = quarantine,
            SalvagedTables = tables,
            SalvagedRows = rows,
            Message =
                $"Recovered. Quarantined={quarantine ?? "(none)"} tables={tables} rows={rows}"
        };
    }

    /// <summary>
    /// Moves the unusable file (and its write-ahead log) into data\corrupt\,
    /// with a timestamped name, instead of deleting it.
    /// </summary>
    private static string? MoveToQuarantine(string databasePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(databasePath);
            var quarantineDirectory = Path.Combine(directory ?? ".", "corrupt");

            Directory.CreateDirectory(quarantineDirectory);

            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var target = Path.Combine(
                quarantineDirectory,
                Path.GetFileNameWithoutExtension(databasePath) + "-" + stamp + ".db");

            SqliteConnection.ClearAllPools();

            File.Move(databasePath, target);

            foreach (var suffix in new[] { "-wal", "-shm" })
            {
                var side = databasePath + suffix;

                if (File.Exists(side))
                    File.Move(side, target + suffix, true);
            }

            AppLogger.Info("Recovery", "Damaged database moved to " + target);

            return target;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Recovery", "The damaged database could not be moved aside: " + ex);
            return null;
        }
    }

    /// <summary>
    /// Copies every readable table from the damaged file into a fresh database.
    /// Only the columns present in both schemas are copied, so an older layout
    /// still yields data. Returns (tables copied, rows copied).
    /// </summary>
    public static (int Tables, long Rows) Salvage(string sourcePath, string targetPath)
    {
        var tables = 0;
        long rows = 0;

        // 1. a fresh target with the current schema
        using (var fresh = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = targetPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString()))
        {
            fresh.Open();

            using var pragma = fresh.CreateCommand();
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
            pragma.ExecuteNonQuery();

            SchemaMigrator.Apply(fresh);
        }

        // 2. attach the damaged file and copy what can still be read
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = targetPath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());

        connection.Open();

        using (var pragma = connection.CreateCommand())
        {
            // A repair import must not be blocked by ordering or missing parents.
            pragma.CommandText = "PRAGMA foreign_keys=OFF;";
            pragma.ExecuteNonQuery();
        }

        try
        {
            using var attach = connection.CreateCommand();
            attach.CommandText = "ATTACH DATABASE $source AS salvage;";
            attach.Parameters.AddWithValue("$source", sourcePath);
            attach.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Recovery", "The damaged database could not be opened at all: " + ex.Message);
            return (0, 0);
        }

        try
        {
            foreach (var table in SalvageOrder)
            {
                try
                {
                    var columns = SharedColumns(connection, table);

                    if (columns.Count == 0)
                        continue;

                    var list = string.Join(", ", columns);

                    using var copy = connection.CreateCommand();
                    copy.CommandTimeout = 30;
                    copy.CommandText =
                        $"INSERT OR IGNORE INTO main.{table} ({list}) SELECT {list} FROM salvage.{table};";

                    var copied = copy.ExecuteNonQuery();

                    if (copied > 0)
                    {
                        tables++;
                        rows += copied;
                    }

                    AppLogger.Info("Recovery", $"Salvaged {copied} row(s) from {table}.");
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Recovery", $"Table '{table}' could not be salvaged: {ex.Message}");
                }
            }
        }
        finally
        {
            try
            {
                using var detach = connection.CreateCommand();
                detach.CommandText = "DETACH DATABASE salvage;";
                detach.ExecuteNonQuery();
            }
            catch
            {
                // the connection is about to close anyway
            }
        }

        return (tables, rows);
    }

    /// <summary>Columns that exist in both the fresh schema and the damaged file.</summary>
    private static List<string> SharedColumns(SqliteConnection connection, string table)
    {
        var target = ColumnNames(connection, "main", table);
        var source = ColumnNames(connection, "salvage", table);

        return target.Where(source.Contains).ToList();
    }

    private static List<string> ColumnNames(SqliteConnection connection, string schema, string table)
    {
        var names = new List<string>();

        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {schema}.table_info({table});";

        using var reader = command.ExecuteReader();

        while (reader.Read())
            names.Add(reader.GetString(1));

        return names;
    }
}
