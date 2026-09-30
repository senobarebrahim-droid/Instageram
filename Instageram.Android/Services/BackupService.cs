// Phase 4: extracted from MainWindow.xaml.cs. Behaviour is unchanged;
// this file only groups one responsibility so the code stays maintainable.
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Instageram;

public static class BackupService
{
    /// <summary>Phase 3: outcome of a restore attempt.</summary>
    public sealed class RestoreResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = "";
        public string? SafetyBackup { get; init; }
    }

    public static string Create() => CreateZip("Instageram_Backup_");

    public static string CreateSafetyCopy() => CreateZip("Instageram_PreRestore_");

    private static string CreateZip(string prefix)
    {
        var temp = Path.Combine(PortablePaths.Cache, "backup_" + Guid.NewGuid().ToString("N"));
        var zip = UniquePath(PortablePaths.Backups, prefix);

        Directory.CreateDirectory(temp);

        try
        {
            CreateConsistentSnapshot(PortablePaths.Database, Path.Combine(temp, "data", "database.db"));
            CopyFile(PortablePaths.Settings, Path.Combine(temp, "config", "settings.json"));

            var manifest = new
            {
                application = "INSTAGERAM",
                created_at = DateTime.UtcNow.ToString("O"),
                note = "Portable backup. Cache is excluded."
            };

            File.WriteAllText(Path.Combine(temp, "backup_manifest.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

            ZipFile.CreateFromDirectory(temp, zip, CompressionLevel.Optimal, false);

            if (!File.Exists(zip) || new FileInfo(zip).Length == 0)
                throw new InvalidOperationException("ÙØ§ÛŒÙ„ Backup Ø§ÛŒØ¬Ø§Ø¯ Ù†Ø´Ø¯.");

            return zip;
        }
        finally
        {
            FileTools.SafeDeleteDirectory(temp);
        }
    }

    private static void CopyFile(string source, string destination)
    {
        if (!File.Exists(source))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, true);
    }

    /// <summary>
    /// Builds a readable, timestamped file name that is guaranteed not to
    /// collide. Two backups created inside the same second (a user clicking
    /// twice, or the automatic backup racing a manual one) used to throw
    /// "the file already exists" â€” found by the test suite.
    /// </summary>
    private static string UniquePath(string directory, string prefix)
    {
        Directory.CreateDirectory(directory);

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var candidate = Path.Combine(directory, prefix + stamp + ".zip");

        var counter = 2;

        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{prefix}{stamp}_{counter}.zip");
            counter++;
        }

        return candidate;
    }

    /// <summary>
    /// Writes a transactionally consistent copy of the database.
    ///
    /// A plain file copy is NOT safe here: in WAL mode the newest committed
    /// data can still live in database.db-wal, so copying database.db alone
    /// silently produces a STALE backup. This really happened and was caught
    /// during testing (a backup contained schema v0 while the live database
    /// was already v2). VACUUM INTO asks SQLite itself for a snapshot.
    /// </summary>
    private static void CreateConsistentSnapshot(string source, string destination)
    {
        if (!File.Exists(source))
            return;

        var targetDirectory = Path.GetDirectoryName(destination);

        if (!string.IsNullOrWhiteSpace(targetDirectory))
            Directory.CreateDirectory(targetDirectory);

        try
        {
            using var con = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = source,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false
            }.ToString());

            con.Open();

            using var cmd = con.CreateCommand();
            cmd.CommandTimeout = 60;
            cmd.CommandText = "VACUUM INTO $target;";
            cmd.Parameters.AddWithValue("$target", destination);
            cmd.ExecuteNonQuery();

            return;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Backup", "VACUUM INTO failed, using the checkpoint fallback: " + ex.Message);
        }

        // Fallback: fold the write-ahead log into the main file first, then copy.
        try
        {
            using (var con = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = source,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false
            }.ToString()))
            {
                con.Open();

                using var cmd = con.CreateCommand();
                cmd.CommandTimeout = 30;
                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                cmd.ExecuteNonQuery();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Backup", "WAL checkpoint before copy failed: " + ex.Message);
        }

        File.Copy(source, destination, true);
    }

    /// <summary>
    /// Phase 3: restores a backup ZIP without ever leaving the user with a
    /// broken database.
    ///
    /// Safety rules, in order:
    ///   1. the ZIP must really contain a healthy SQLite database,
    ///   2. the current state is copied to a safety backup first,
    ///   3. the restored database is migrated forward if it is older,
    ///   4. any failure rolls back to the safety copy automatically.
    /// </summary>
    public static RestoreResult Restore(string zipPath)
    {
        var temp = Path.Combine(PortablePaths.Cache, "restore_" + Guid.NewGuid().ToString("N"));
        string? safety = null;

        try
        {
            if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
                return Failed("ÙØ§ÛŒÙ„ Ù¾Ø´ØªÛŒØ¨Ø§Ù† Ù¾ÛŒØ¯Ø§ Ù†Ø´Ø¯.");

            Directory.CreateDirectory(temp);
            ZipFile.ExtractToDirectory(zipPath, temp, true);

            var source = Path.Combine(temp, "data", "database.db");

            if (!File.Exists(source))
                return Failed("Ø§ÛŒÙ† ÙØ§ÛŒÙ„ Ù¾Ø´ØªÛŒØ¨Ø§Ù† Ù…Ø¹ØªØ¨Ø± Ù†ÛŒØ³Øª: data\\database.db Ø¯Ø± Ø¢Ù† ÙˆØ¬ÙˆØ¯ Ù†Ø¯Ø§Ø±Ø¯.");

            if (!LooksLikeSqlite(source))
                return Failed("ÙØ§ÛŒÙ„ Ø¯ÛŒØªØ§Ø¨ÛŒØ³ Ø¯Ø§Ø®Ù„ Ù¾Ø´ØªÛŒØ¨Ø§Ù†ØŒ ÛŒÚ© ÙØ§ÛŒÙ„ SQLite Ù…Ø¹ØªØ¨Ø± Ù†ÛŒØ³Øª.");

            if (!IsHealthy(source, out var problem))
                return Failed("Ø¯ÛŒØªØ§Ø¨ÛŒØ³ Ø¯Ø§Ø®Ù„ Ù¾Ø´ØªÛŒØ¨Ø§Ù† Ø³Ø§Ù„Ù… Ù†ÛŒØ³Øª: " + problem);

            if (File.Exists(PortablePaths.Database))
                safety = CreateSafetyCopy();

            // No pooled connection may keep the live database file open while
            // it is being replaced.
            SqliteConnection.ClearAllPools();

            File.Copy(source, PortablePaths.Database, true);

            // Older packages may still carry a write-ahead log; it belongs to
            // the database and must travel with it, otherwise data is lost.
            RemoveSideFiles(PortablePaths.Database);

            var restoredWal = source + "-wal";

            if (File.Exists(restoredWal))
                File.Copy(restoredWal, PortablePaths.Database + "-wal", true);

            var restoredSettings = Path.Combine(temp, "config", "settings.json");

            if (File.Exists(restoredSettings))
                File.Copy(restoredSettings, PortablePaths.Settings, true);

            // A backup may come from an older version: migrate it forward.
            DatabaseService.Initialize();

            // A restored database starts without catalogue data, so it is
            // refilled immediately (countries table included).
            CountryCatalog.Load();
            CountryCatalog.SyncToDatabase();

            AppLogger.Info("Restore", "Backup restored from " + zipPath);

            return new RestoreResult
            {
                Success = true,
                SafetyBackup = safety,
                Message = "Ø¨Ø§Ø²ÛŒØ§Ø¨ÛŒ Ø§Ù†Ø¬Ø§Ù… Ø´Ø¯." + Environment.NewLine +
                          "Ù†Ø³Ø®Ù‡ Ø§Ù…Ù†ÛŒØªÛŒ ÙˆØ¶Ø¹ÛŒØª Ù‚Ø¨Ù„ÛŒ: " + (safety ?? "(Ù†Ø¯Ø§Ø±Ø¯)")
            };
        }
        catch (Exception ex)
        {
            AppLogger.Error("Restore", ex.ToString());

            var rollback = TryRollback(safety);

            return Failed(
                "Ø¨Ø§Ø²ÛŒØ§Ø¨ÛŒ Ù†Ø§Ù…ÙˆÙÙ‚ Ø¨ÙˆØ¯: " + ex.Message + Environment.NewLine + rollback,
                safety);
        }
        finally
        {
            FileTools.SafeDeleteDirectory(temp);
        }
    }

    private static RestoreResult Failed(string message, string? safety = null) =>
        new() { Success = false, Message = message, SafetyBackup = safety };

    private static bool LooksLikeSqlite(string file)
    {
        using var stream = File.OpenRead(file);
        var header = new byte[16];

        if (stream.Read(header, 0, header.Length) != header.Length)
            return false;

        return Encoding.ASCII.GetString(header) == "SQLite format 3\0";
    }

    private static bool IsHealthy(string file, out string problem)
    {
        problem = "";

        try
        {
            using var con = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = file,
                Mode = SqliteOpenMode.ReadOnly,
                // Pooling would keep a handle on the extracted file open and
                // make the temp folder impossible to delete (found in testing).
                Pooling = false
            }.ToString());

            con.Open();

            using var cmd = con.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";

            var result = cmd.ExecuteScalar()?.ToString() ?? "";

            if (string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                return true;

            problem = result;
            return false;
        }
        catch (Exception ex)
        {
            problem = ex.Message;
            return false;
        }
    }

    private static void RemoveSideFiles(string database)
    {
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var side = database + suffix;

            if (File.Exists(side))
                File.Delete(side);
        }
    }

    private static string TryRollback(string? safety)
    {
        if (string.IsNullOrWhiteSpace(safety) || !File.Exists(safety))
            return "Ù‡ÛŒÚ† Ù†Ø³Ø®Ù‡ Ø§Ù…Ù†ÛŒØªÛŒ Ø¨Ø±Ø§ÛŒ Ø¨Ø§Ø²Ú¯Ø´Øª ÙˆØ¬ÙˆØ¯ Ù†Ø¯Ø§Ø´Øª.";

        var temp = Path.Combine(PortablePaths.Cache, "rollback_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(temp);
            ZipFile.ExtractToDirectory(safety, temp, true);

            var source = Path.Combine(temp, "data", "database.db");

            if (!File.Exists(source))
                return "Ù†Ø³Ø®Ù‡ Ø§Ù…Ù†ÛŒØªÛŒ Ù…Ø­ØªÙˆØ§ÛŒ Ø¯ÛŒØªØ§Ø¨ÛŒØ³ Ù†Ø¯Ø§Ø´Øª.";

            File.Copy(source, PortablePaths.Database, true);
            RemoveSideFiles(PortablePaths.Database);
            DatabaseService.Initialize();

            return "ÙˆØ¶Ø¹ÛŒØª Ù‚Ø¨Ù„ÛŒ Ø¨Ù‡â€ŒØµÙˆØ±Øª Ø®ÙˆØ¯Ú©Ø§Ø± Ø¨Ø§Ø²Ú¯Ø±Ø¯Ø§Ù†Ø¯Ù‡ Ø´Ø¯.";
        }
        catch (Exception ex)
        {
            AppLogger.Error("Rollback", ex.ToString());
            return "Ø¨Ø§Ø²Ú¯Ø´Øª Ø®ÙˆØ¯Ú©Ø§Ø± Ù‡Ù… Ù†Ø§Ù…ÙˆÙÙ‚ Ø¨ÙˆØ¯: " + ex.Message;
        }
        finally
        {
            FileTools.SafeDeleteDirectory(temp);
        }
    }
}
