using System.IO;
using Microsoft.Data.Sqlite;

namespace Instageram.Tests;

/// <summary>
/// Prepares one deterministic environment for every test: a private database
/// inside the test output folder, no legacy migration, and English strings.
/// </summary>
public static class TestEnvironment
{
    private static readonly object Gate = new();
    private static bool _ready;

    public static void EnsureReady()
    {
        lock (Gate)
        {
            if (_ready)
                return;

            // Without this the application would copy the developer's real
            // database (an absolute path on this machine) into the test run.
            Environment.SetEnvironmentVariable("INSTAGERAM_SKIP_LEGACY_MIGRATION", "1");

            PortablePaths.Initialize();

            SqliteConnection.ClearAllPools();

            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var file = PortablePaths.Database + suffix;

                if (File.Exists(file))
                    File.Delete(file);
            }

            DatabaseService.Initialize();
            AppSettings.Load();
            Localization.Load("en");

            _ready = true;
        }
    }

    /// <summary>A unique path under the system temp folder.</summary>
    public static string TempFile(string extension)
    {
        var directory = Path.Combine(Path.GetTempPath(), "instageram-tests");
        Directory.CreateDirectory(directory);

        return Path.Combine(directory, Guid.NewGuid().ToString("N") + extension);
    }

    /// <summary>A unique name, so tests never collide on the shared database.</summary>
    public static string Unique(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..8];
}
