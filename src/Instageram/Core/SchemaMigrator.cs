using Microsoft.Data.Sqlite;

namespace Instageram;

/// <summary>
/// Phase 2: versioned, data-preserving schema management.
///
/// The schema version lives in SQLite's own "PRAGMA user_version".
/// Every step is additive and idempotent:
///   * an existing database keeps ALL of its rows,
///   * running the migrator twice changes nothing,
///   * a database created before Phase 2 (user_version = 0) is upgraded
///     in place instead of being recreated.
///
/// database\schema.sql is the human-readable mirror of the resulting state.
/// </summary>
public static class SchemaMigrator
{
    public const int CurrentVersion = 2;

    /// <summary>Applies every pending step and returns the resulting version.</summary>
    public static int Apply(SqliteConnection connection)
    {
        var version = GetUserVersion(connection);

        using var transaction = connection.BeginTransaction();

        try
        {
            if (version < 1)
            {
                ApplyVersion1(connection);
                version = 1;
            }

            if (version < 2)
            {
                ApplyVersion2(connection);
                version = 2;
            }

            SetUserVersion(connection, version);
            transaction.Commit();

            return version;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    // ---------------------------------------------------------------- v1
    // The two tables the application has always used, plus the columns that
    // database\schema.sql described but the code never created.
    private static void ApplyVersion1(SqliteConnection connection)
    {
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS projects (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                project_name TEXT NOT NULL,
                instagram_username TEXT NOT NULL,
                instagram_url TEXT NOT NULL,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS campaigns (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id INTEGER NOT NULL,
                campaign_name TEXT NOT NULL,
                countries TEXT NOT NULL DEFAULT '',
                target_number INTEGER NOT NULL DEFAULT 0,
                current_number INTEGER NOT NULL DEFAULT 0,
                status TEXT NOT NULL DEFAULT 'Draft',
                start_date TEXT NOT NULL DEFAULT '',
                instagram_url TEXT NOT NULL DEFAULT '',
                FOREIGN KEY(project_id) REFERENCES projects(id)
            );
            """);

        EnsureColumn(connection, "projects", "updated_at", "TEXT");
        EnsureColumn(connection, "projects", "status", "TEXT NOT NULL DEFAULT 'Active'");
        EnsureColumn(connection, "projects", "notes", "TEXT");

        EnsureColumn(connection, "campaigns", "countries", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "campaigns", "instagram_url", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "campaigns", "created_at", "TEXT");
        EnsureColumn(connection, "campaigns", "updated_at", "TEXT");
        EnsureColumn(connection, "campaigns", "end_date", "TEXT");
        EnsureColumn(connection, "campaigns", "notes", "TEXT");
    }

    // ---------------------------------------------------------------- v2
    // The seven tables that database\schema.sql promised and the application
    // never created, so analytics, settings and logging have real storage.
    private static void ApplyVersion2(SqliteConnection connection)
    {
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS countries (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                country_code TEXT NOT NULL UNIQUE,
                country_name TEXT NOT NULL,
                name_fa TEXT,
                language TEXT,
                timezone TEXT,
                region TEXT,
                is_target_market INTEGER NOT NULL DEFAULT 0,
                priority INTEGER NOT NULL DEFAULT 999,
                enabled INTEGER NOT NULL DEFAULT 1,
                excluded INTEGER NOT NULL DEFAULT 0,
                updated_at TEXT
            );

            CREATE TABLE IF NOT EXISTS campaign_countries (
                campaign_id INTEGER NOT NULL,
                country_id INTEGER NOT NULL,
                is_excluded INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY(campaign_id, country_id)
            );

            CREATE TABLE IF NOT EXISTS campaign_statistics (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                campaign_id INTEGER NOT NULL,
                date TEXT NOT NULL,
                followers INTEGER NOT NULL DEFAULT 0,
                engagement_rate REAL NOT NULL DEFAULT 0,
                growth INTEGER NOT NULL DEFAULT 0,
                country TEXT,
                notes TEXT
            );

            CREATE TABLE IF NOT EXISTS profile_statistics (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id INTEGER NOT NULL,
                date TEXT NOT NULL,
                followers INTEGER NOT NULL DEFAULT 0,
                following INTEGER NOT NULL DEFAULT 0,
                post_count INTEGER NOT NULL DEFAULT 0,
                engagement_rate REAL NOT NULL DEFAULT 0,
                notes TEXT
            );

            CREATE TABLE IF NOT EXISTS reports (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                campaign_id INTEGER,
                report_name TEXT NOT NULL,
                report_format TEXT NOT NULL,
                file_path TEXT NOT NULL,
                generated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS app_settings (
                setting_key TEXT PRIMARY KEY,
                setting_value TEXT,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS logs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                logged_at TEXT NOT NULL,
                level TEXT NOT NULL,
                module TEXT,
                message TEXT NOT NULL,
                details TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_campaigns_project_id
                ON campaigns(project_id);

            CREATE INDEX IF NOT EXISTS idx_campaign_statistics_campaign_date
                ON campaign_statistics(campaign_id, date);

            CREATE INDEX IF NOT EXISTS idx_profile_statistics_project_date
                ON profile_statistics(project_id, date);

            CREATE INDEX IF NOT EXISTS idx_logs_logged_at
                ON logs(logged_at);
            """);

        BackfillTimestamps(connection);
    }

    /// <summary>
    /// Fills the new timestamp columns from data that already exists, so the
    /// upgraded rows are usable instead of NULL.
    /// </summary>
    private static void BackfillTimestamps(SqliteConnection connection)
    {
        Execute(connection, """
            UPDATE campaigns
               SET created_at = start_date
             WHERE created_at IS NULL
               AND start_date IS NOT NULL
               AND start_date <> '';

            UPDATE campaigns
               SET updated_at = created_at
             WHERE updated_at IS NULL
               AND created_at IS NOT NULL;

            UPDATE projects
               SET updated_at = created_at
             WHERE updated_at IS NULL
               AND created_at IS NOT NULL;
            """);
    }

    // ------------------------------------------------------------- helpers
    private static int GetUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";

        return Convert.ToInt32(command.ExecuteScalar() ?? 0);
    }

    private static void SetUserVersion(SqliteConnection connection, int version)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version = " + version + ";";
        command.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void EnsureColumn(
        SqliteConnection connection,
        string table,
        string column,
        string definition)
    {
        if (ColumnExists(connection, table, column))
            return;

        Execute(connection, $"ALTER TABLE {table} ADD COLUMN {column} {definition};");
    }

    private static bool ColumnExists(
        SqliteConnection connection,
        string table,
        string column)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(" + table + ");";

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
