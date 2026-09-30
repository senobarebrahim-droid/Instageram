// Phase 4: extracted from MainWindow.xaml.cs. Behaviour is unchanged;
// this file only groups one responsibility so the code stays maintainable.
using System.IO;
using Microsoft.Data.Sqlite;

namespace Instageram;

public static class AppLogger
{
    public static void Info(string module, string message) => Write("INFO", module, message);
    public static void Error(string module, string message) => Write("ERROR", module, message);

    private static void Write(string level, string module, string message)
    {
        try
        {
            File.AppendAllText(
                PortablePaths.LogFile,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {level} | {module} | {message}{Environment.NewLine}");
        }
        catch { }

        // Phase 2: entries are also stored in the database (logs table).
        // Best effort only; the file above remains the primary log.
        WriteToDatabase(level, module, message);
    }

    private static void WriteToDatabase(string level, string module, string message)
    {
        try
        {
            if (!File.Exists(PortablePaths.Database))
                return;

            using var con = new SqliteConnection(DatabaseService.ConnectionString);
            con.Open();

            using var cmd = con.CreateCommand();
            cmd.CommandTimeout = 2; // never stall the UI because of logging

            cmd.CommandText = """
                INSERT INTO logs (logged_at, level, module, message)
                VALUES ($at, $level, $module, $message);
                """;

            cmd.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$level", level);
            cmd.Parameters.AddWithValue("$module", module);
            cmd.Parameters.AddWithValue("$message", message);

            cmd.ExecuteNonQuery();
        }
        catch
        {
            // logging must never break the application
        }
    }
}
