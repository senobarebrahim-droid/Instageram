using System.IO;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Instageram.Tests;

public class ErrorMessagesTests
{
    public ErrorMessagesTests() => TestEnvironment.EnsureReady();

    [Fact]
    public void A_damaged_database_gets_the_damage_message()
    {
        var described = ErrorMessages.Describe(new SqliteException("file is not a database", 26));

        Assert.Equal(Localization.T("error_database_corrupt"), described);
    }

    [Fact]
    public void A_locked_database_gets_the_in_use_message()
    {
        var described = ErrorMessages.Describe(new SqliteException("database is locked", 5));

        Assert.Equal(Localization.T("error_database_locked"), described);
    }

    [Fact]
    public void A_missing_table_gets_the_schema_message()
    {
        var described = ErrorMessages.Describe(new SqliteException("no such table: campaigns", 1));

        Assert.Equal(Localization.T("error_database_schema"), described);
    }

    [Fact]
    public void A_disk_problem_gets_the_space_message()
    {
        var described = ErrorMessages.Describe(new SqliteException("database or disk is full", 13));

        Assert.Equal(Localization.T("error_disk_full"), described);
    }

    [Fact]
    public void A_read_only_location_gets_the_permission_message()
    {
        var described = ErrorMessages.Describe(new SqliteException("attempt to write a readonly database", 8));

        Assert.Equal(Localization.T("error_readonly"), described);
    }

    [Fact]
    public void A_denied_path_gets_the_access_message()
    {
        var described = ErrorMessages.Describe(new UnauthorizedAccessException("denied"));

        Assert.Equal(Localization.T("error_access_denied"), described);
    }

    [Fact]
    public void A_file_problem_gets_the_locked_file_message()
    {
        var described = ErrorMessages.Describe(new IOException("The process cannot access the file"));

        Assert.Equal(Localization.T("error_file_locked"), described);
    }

    [Fact]
    public void An_unknown_problem_gets_the_generic_message()
    {
        var described = ErrorMessages.Describe(new InvalidOperationException("something odd"));

        Assert.Equal(Localization.T("error_unexpected"), described);
    }

    [Fact]
    public void A_null_exception_still_produces_a_sentence()
    {
        Assert.False(string.IsNullOrWhiteSpace(ErrorMessages.Describe(null)));
    }

    [Fact]
    public void Every_message_that_reaches_the_user_stays_short()
    {
        // A wall of technical text in a dialog is what this class exists to stop.
        foreach (var exception in new Exception[]
                 {
                     new SqliteException("file is not a database", 26),
                     new IOException("locked"),
                     new InvalidOperationException("boom")
                 })
        {
            var described = ErrorMessages.Describe(exception);

            Assert.True(described.Length < 200, "message is too long: " + described);
            Assert.DoesNotContain("   at ", described);
        }
    }

    [Fact]
    public void The_user_message_points_at_the_log_file()
    {
        var message = ErrorMessages.ForUser(new InvalidOperationException("boom"));

        Assert.Contains(Localization.T("error_details_in_log"), message);
        Assert.Contains("application.log", message);
    }

    [Fact]
    public void No_technical_detail_is_lost()
    {
        // The full exception must still be reachable through the log file path
        // that the dialog mentions.
        var message = ErrorMessages.ForUser(new InvalidOperationException("boom"));

        Assert.Contains(PortablePaths.Logs, message);
    }
}
