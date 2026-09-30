using System.IO;
using Microsoft.Data.Sqlite;

namespace Instageram;

/// <summary>
/// Phase 4: turns technical exceptions into something a user can act on.
/// The technical detail always stays in the log; the window shows one clear
/// sentence and tells the user where the detail is.
/// </summary>
public static class ErrorMessages
{
    private const string LogFile = "logs\\application.log";

    public static string Describe(Exception? exception)
    {
        if (exception == null)
            return Localization.T("error_unexpected");

        if (exception is SqliteException)
        {
            var text = (exception.Message ?? "").ToLowerInvariant();

            if (Contains(text, "not a database", "malformed", "corrupt", "encrypted"))
                return Localization.T("error_database_corrupt");

            if (Contains(text, "locked", "busy"))
                return Localization.T("error_database_locked");

            if (Contains(text, "no such table", "no such column"))
                return Localization.T("error_database_schema");

            if (Contains(text, "disk", "full"))
                return Localization.T("error_disk_full");

            if (Contains(text, "readonly", "read-only", "unable to open"))
                return Localization.T("error_readonly");

            return Localization.T("error_database_generic");
        }

        if (exception is UnauthorizedAccessException)
            return Localization.T("error_access_denied");

        if (exception is OutOfMemoryException)
            return Localization.T("error_out_of_memory");

        if (exception is IOException)
            return Localization.T("error_file_locked");

        return Localization.T("error_unexpected");
    }

    /// <summary>The text to show in a message box.</summary>
    public static string ForUser(Exception? exception) =>
        Describe(exception) + Environment.NewLine + Environment.NewLine +
        Localization.T("error_details_in_log") + Environment.NewLine +
        Path.Combine(PortablePaths.Logs, "application.log");

    private static bool Contains(string text, params string[] needles) =>
        needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
}
