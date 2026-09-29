using System.IO;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Instageram.Tests;

public class DatabaseHealthTests
{
    [Fact]
    public void A_valid_database_is_healthy()
    {
        var path = CreateDatabase();

        Assert.True(DatabaseHealth.IsHealthy(path, out var problem), problem);
        Assert.True(DatabaseHealth.LooksLikeSqlite(path));
    }

    [Fact]
    public void A_file_of_garbage_is_not_a_database()
    {
        var path = TestEnvironment.TempFile(".db");
        File.WriteAllText(path, new string('x', 4096));

        Assert.False(DatabaseHealth.LooksLikeSqlite(path));
        Assert.False(DatabaseHealth.IsHealthy(path, out var problem));
        Assert.False(string.IsNullOrWhiteSpace(problem));
    }

    [Fact]
    public void A_missing_file_is_not_healthy()
    {
        Assert.False(DatabaseHealth.IsHealthy(TestEnvironment.TempFile(".db"), out _));
    }

    [Fact]
    public void A_truncated_database_is_not_healthy()
    {
        var path = CreateDatabase();
        var bytes = File.ReadAllBytes(path);

        // keep only the header: SQLite cannot read the rest
        File.WriteAllBytes(path, bytes[..100]);

        Assert.False(DatabaseHealth.IsHealthy(path, out _));
    }

    [Fact]
    public void EnsureUsable_leaves_a_healthy_database_alone()
    {
        var path = CreateDatabase();
        var before = File.GetLastWriteTimeUtc(path);

        var result = DatabaseHealth.EnsureUsable(path);

        Assert.False(result.Recovered);
        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void Recovery_quarantines_the_damaged_file_instead_of_deleting_it()
    {
        var path = TestEnvironment.TempFile(".db");
        File.WriteAllText(path, new string('x', 2048));

        var result = DatabaseHealth.EnsureUsable(path);

        Assert.True(result.Recovered);
        Assert.True(result.Quarantined);
        Assert.NotNull(result.QuarantinePath);
        Assert.True(File.Exists(result.QuarantinePath!), "the damaged file must still exist");

        // and the path now holds a usable database with the current schema
        Assert.True(DatabaseHealth.IsHealthy(path, out var problem), problem);

        using var check = Open(path);
        Assert.True(TableExists(check, "campaigns"));
        Assert.True(TableExists(check, "campaign_statistics"));
        Assert.True(TableExists(check, "logs"));
    }

    [Fact]
    public void Salvage_copies_readable_rows_into_a_fresh_database()
    {
        var source = CreateDatabase();
        Seed(source, "Salvage campaign", "salvageuser");

        var target = TestEnvironment.TempFile(".db");

        var (_, rows) = DatabaseHealth.Salvage(source, target);

        Assert.True(rows > 0, "nothing was salvaged");
        Assert.True(DatabaseHealth.IsHealthy(target, out var problem), problem);

        using var connection = Open(target);

        Assert.True(Count(connection, "campaigns") > 0, "the campaign was not salvaged");
        Assert.True(Count(connection, "projects") > 0, "the project was not salvaged");
    }

    [Fact]
    public void Salvage_copies_only_the_columns_both_schemas_share()
    {
        // an older layout: campaigns without instagram_url / created_at
        var source = TestEnvironment.TempFile(".db");

        using (var connection = Open(source))
        {
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
                    VALUES ('Old project', 'olduser', 'https://www.instagram.com/olduser/', '2026-01-01');

                INSERT INTO campaigns (project_id, campaign_name, countries, target_number, current_number, status, start_date)
                    VALUES (1, 'Old campaign', 'Iran', 2000, 5, 'Active', '2026-01-01');
                """);
        }

        var target = TestEnvironment.TempFile(".db");
        var (_, rows) = DatabaseHealth.Salvage(source, target);

        Assert.True(rows > 0, "an older schema must still be salvageable");

        using var check = Open(target);
        Assert.Equal(1L, Count(check, "projects"));
        Assert.Equal(1L, Count(check, "campaigns"));
        Assert.Equal("Old campaign", Text(check, "SELECT campaign_name FROM campaigns LIMIT 1"));
    }

    [Fact]
    public void Salvage_from_an_unreadable_file_reports_zero_instead_of_throwing()
    {
        var source = TestEnvironment.TempFile(".db");
        File.WriteAllText(source, new string('x', 4096));

        var target = TestEnvironment.TempFile(".db");
        var (tables, rows) = DatabaseHealth.Salvage(source, target);

        Assert.Equal(0, tables);
        Assert.Equal(0L, rows);
    }

    [Fact]
    public void A_locked_database_is_unreadable_and_is_never_quarantined()
    {
        // Regression: the header peek used File.OpenRead (share mode Read only),
        // so a database held by another connection looked like "not a SQLite
        // database" and was quarantined, replacing perfectly good user data.
        var path = CreateDatabase();
        Seed(path, "Locked campaign", "lockeduser");

        using (var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var state = DatabaseHealth.Inspect(path, out var problem);

            Assert.Equal(DatabaseHealth.DatabaseState.Unreadable, state);
            Assert.False(string.IsNullOrWhiteSpace(problem));

            var result = DatabaseHealth.EnsureUsable(path);

            Assert.False(result.Recovered);
            Assert.False(result.Quarantined);
            Assert.Null(result.QuarantinePath);
        }

        // released: still the very same database, untouched
        Assert.Equal(DatabaseHealth.DatabaseState.Healthy, DatabaseHealth.Inspect(path, out _));

        using var check = Open(path);
        Assert.Equal(1L, Count(check, "campaigns"));
        Assert.Equal("Locked campaign", Text(check, "SELECT campaign_name FROM campaigns LIMIT 1"));
    }

    [Fact]
    public void EnsureUsable_changes_nothing_for_a_healthy_database()
    {
        var path = CreateDatabase();
        Seed(path, "Untouched campaign", "untoucheduser");

        var sizeBefore = new FileInfo(path).Length;
        var hashBefore = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path));

        var result = DatabaseHealth.EnsureUsable(path);

        Assert.False(result.Recovered);

        var hashAfter = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path));

        Assert.Equal(sizeBefore, new FileInfo(path).Length);
        Assert.Equal(hashBefore, hashAfter);
    }

    // ------------------------------------------------------------- helpers

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());

        connection.Open();

        return connection;
    }

    private static string CreateDatabase()
    {
        var path = TestEnvironment.TempFile(".db");

        using var connection = Open(path);
        SchemaMigrator.Apply(connection);

        return path;
    }

    private static bool TableExists(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", table);

        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    private static void Seed(string path, string campaign, string username)
    {
        using var connection = Open(path);

        using var project = connection.CreateCommand();
        project.CommandText = """
            INSERT INTO projects (project_name, instagram_username, instagram_url, created_at)
            VALUES ($name, $user, $url, $now);
            """;
        project.Parameters.AddWithValue("$name", campaign);
        project.Parameters.AddWithValue("$user", username);
        project.Parameters.AddWithValue("$url", "https://www.instagram.com/" + username + "/");
        project.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        project.ExecuteNonQuery();

        using var row = connection.CreateCommand();
        row.CommandText = """
            INSERT INTO campaigns
                (project_id, campaign_name, countries, target_number, current_number, status, start_date, instagram_url)
            VALUES (1, $name, 'Iran', 1000, 0, 'Active', '2026-01-01', 'https://www.instagram.com/x/');
            """;
        row.Parameters.AddWithValue("$name", campaign);
        row.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long Count(SqliteConnection connection, string table) =>
        Convert.ToInt64(Scalar(connection, "SELECT COUNT(*) FROM " + table));

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return command.ExecuteScalar();
    }

    private static string Text(SqliteConnection connection, string sql) =>
        Scalar(connection, sql)?.ToString() ?? "";
}
