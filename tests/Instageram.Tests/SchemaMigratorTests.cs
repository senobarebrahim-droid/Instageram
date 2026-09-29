using Microsoft.Data.Sqlite;
using Xunit;

namespace Instageram.Tests;

public class SchemaMigratorTests
{
    private static readonly string[] ExpectedTables =
    {
        "app_settings",
        "campaign_countries",
        "campaign_statistics",
        "campaigns",
        "countries",
        "logs",
        "profile_statistics",
        "projects",
        "reports"
    };

    [Fact]
    public void Creates_every_table_and_records_the_current_version()
    {
        using var connection = OpenTempDatabase();

        var version = SchemaMigrator.Apply(connection);

        Assert.Equal(SchemaMigrator.CurrentVersion, version);
        Assert.Equal(2, version);
        Assert.Equal(
            ExpectedTables.OrderBy(name => name),
            TableNames(connection).OrderBy(name => name));
    }

    [Fact]
    public void Adds_the_indexes()
    {
        using var connection = OpenTempDatabase();
        SchemaMigrator.Apply(connection);

        var indexes = Query(connection, "SELECT name FROM sqlite_master WHERE type = 'index' AND name NOT LIKE 'sqlite_%'");

        Assert.Contains("idx_campaigns_project_id", indexes);
        Assert.Contains("idx_campaign_statistics_campaign_date", indexes);
        Assert.Contains("idx_profile_statistics_project_date", indexes);
        Assert.Contains("idx_logs_logged_at", indexes);
    }

    [Fact]
    public void Running_twice_changes_nothing()
    {
        using var connection = OpenTempDatabase();

        SchemaMigrator.Apply(connection);
        var tablesAfterFirst = TableNames(connection).OrderBy(name => name).ToList();
        var version = SchemaMigrator.Apply(connection);

        Assert.Equal(SchemaMigrator.CurrentVersion, version);
        Assert.Equal(tablesAfterFirst, TableNames(connection).OrderBy(name => name).ToList());
    }

    [Fact]
    public void Upgrades_a_legacy_database_without_losing_rows()
    {
        using var connection = OpenTempDatabase();

        // The shape the application used before Phase 2: no instagram_url on
        // campaigns, no timestamps, no extra tables, user_version = 0.
        Execute(connection, """
            CREATE TABLE projects (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                project_name TEXT NOT NULL,
                instagram_username TEXT NOT NULL,
                instagram_url TEXT NOT NULL,
                created_at TEXT NOT NULL);

            CREATE TABLE campaigns (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id INTEGER NOT NULL,
                campaign_name TEXT NOT NULL,
                countries TEXT NOT NULL,
                target_number INTEGER NOT NULL,
                current_number INTEGER NOT NULL,
                status TEXT NOT NULL,
                start_date TEXT NOT NULL);

            INSERT INTO projects (project_name, instagram_username, instagram_url, created_at)
                VALUES ('Legacy project', 'legacyuser', 'https://www.instagram.com/legacyuser/', '2026-01-01');

            INSERT INTO campaigns (project_id, campaign_name, countries, target_number, current_number, status, start_date)
                VALUES (1, 'Legacy campaign', 'Iran', 1000, 10, 'Active', '2026-01-01');
            """);

        var version = SchemaMigrator.Apply(connection);

        Assert.Equal(SchemaMigrator.CurrentVersion, version);

        Assert.True(ColumnExists(connection, "campaigns", "instagram_url"), "campaigns.instagram_url was not added");
        Assert.True(ColumnExists(connection, "campaigns", "created_at"), "campaigns.created_at was not added");
        Assert.True(ColumnExists(connection, "projects", "status"), "projects.status was not added");

        Assert.Equal(1L, Scalar(connection, "SELECT COUNT(*) FROM campaigns"));
        Assert.Equal(1L, Scalar(connection, "SELECT COUNT(*) FROM projects"));

        // the timestamp columns are backfilled from the data that already existed
        Assert.Equal(
            "2026-01-01",
            Text(connection, "SELECT created_at FROM campaigns WHERE id = 1"));

        Assert.Equal(
            "2026-01-01",
            Text(connection, "SELECT updated_at FROM projects WHERE id = 1"));
    }

    [Fact]
    public void A_failed_migration_rolls_back_completely()
    {
        using var connection = OpenTempDatabase();

        // A campaigns table without start_date: the additive column step
        // succeeds, but the timestamp backfill cannot read start_date, so the
        // migration fails part way through.
        Execute(connection, """
            CREATE TABLE campaigns (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id INTEGER NOT NULL,
                campaign_name TEXT NOT NULL,
                countries TEXT NOT NULL,
                target_number INTEGER NOT NULL,
                current_number INTEGER NOT NULL,
                status TEXT NOT NULL);
            """);

        Assert.ThrowsAny<SqliteException>(() => SchemaMigrator.Apply(connection));

        // nothing was committed: no version bump and none of the new tables
        Assert.Equal(0L, Scalar(connection, "PRAGMA user_version"));
        Assert.DoesNotContain("logs", TableNames(connection));
        Assert.DoesNotContain("countries", TableNames(connection));
    }

    private static SqliteConnection OpenTempDatabase()
    {
        var path = TestEnvironment.TempFile(".db");

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false
        }.ToString());

        connection.Open();

        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static List<string> TableNames(SqliteConnection connection) =>
        Query(connection, "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'");

    private static List<string> Query(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        using var reader = command.ExecuteReader();

        var values = new List<string>();

        while (reader.Read())
            values.Add(reader.GetString(0));

        return values;
    }

    private static bool ColumnExists(SqliteConnection connection, string table, string column)
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

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToInt64(command.ExecuteScalar() ?? 0L);
    }

    private static string Text(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return command.ExecuteScalar()?.ToString() ?? "";
    }
}
