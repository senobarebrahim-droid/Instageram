using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Instageram;

/// <summary>
/// Phase 2: config\settings.json is finally READ, not just written.
/// Phase 3: the Settings page can write it back, and the values are mirrored
/// into the app_settings table.
///
/// Design rules:
///   * unknown keys are preserved on save,
///   * missing or malformed keys keep a safe default,
///   * a broken settings file never prevents the application from starting.
/// </summary>
public static class AppSettings
{
    public static string Language { get; private set; } = "fa";
    public static string Theme { get; private set; } = "Light";
    public static bool AutomaticBackup { get; private set; } = true;
    public static bool IntegrityCheckEnabled { get; private set; } = true;

    /// <summary>
    /// Which tab the window opens on. Read from settings.json; not rewritten
    /// by the Settings page, so it stays a file-level preference.
    /// </summary>
    public static string StartupMode { get; private set; } = "Dashboard";

    /// <summary>Where the values came from, for logging.</summary>
    public static string Source { get; private set; } = "built-in defaults";

    public static void Load()
    {
        try
        {
            if (!File.Exists(PortablePaths.Settings))
                return;

            using var document = JsonDocument.Parse(
                File.ReadAllText(PortablePaths.Settings));

            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return;

            Language = ReadString(root, "language", Language);
            Theme = ReadString(root, "theme", Theme);
            AutomaticBackup = ReadBool(root, "auto_backup", AutomaticBackup);
            IntegrityCheckEnabled = ReadBool(root, "integrity_check_enabled", IntegrityCheckEnabled);
            StartupMode = ReadString(root, "startup_mode", StartupMode);

            Source = PortablePaths.Settings;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Settings", "settings.json could not be read; defaults are used: " + ex.Message);
        }
    }

    public static string Summary() =>
        $"settings={Source} language={Language} theme={Theme} " +
        $"auto_backup={AutomaticBackup} integrity_check={IntegrityCheckEnabled} " +
        $"startup_mode={StartupMode}";

    /// <summary>Phase 3: applies values coming from the Settings page.</summary>
    public static void Set(string language, string theme, bool automaticBackup, bool integrityCheck)
    {
        if (!string.IsNullOrWhiteSpace(language))
            Language = language.Trim();

        if (!string.IsNullOrWhiteSpace(theme))
            Theme = theme.Trim();

        AutomaticBackup = automaticBackup;
        IntegrityCheckEnabled = integrityCheck;
    }

    /// <summary>
    /// Writes config\settings.json back to disk. Keys the user added by hand
    /// are preserved; only the four managed keys are replaced.
    /// </summary>
    public static bool Save()
    {
        try
        {
            var root = new JsonObject();

            if (File.Exists(PortablePaths.Settings))
            {
                try
                {
                    if (JsonNode.Parse(File.ReadAllText(PortablePaths.Settings)) is JsonObject existing)
                        root = existing;
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Settings", "settings.json was unreadable and is rebuilt: " + ex.Message);
                }
            }

            root["language"] = Language;
            root["theme"] = Theme;
            root["auto_backup"] = AutomaticBackup;
            root["integrity_check_enabled"] = IntegrityCheckEnabled;

            File.WriteAllText(
                PortablePaths.Settings,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            Source = PortablePaths.Settings;

            SyncToDatabase();

            AppLogger.Info("Settings", "Settings saved. " + Summary());

            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Settings", "Settings could not be saved: " + ex);
            return false;
        }
    }

    /// <summary>Mirrors the managed settings into the app_settings table.</summary>
    public static int SyncToDatabase()
    {
        try
        {
            if (!File.Exists(PortablePaths.Database))
                return 0;

            using var con = new SqliteConnection(DatabaseService.ConnectionString);
            con.Open();

            using var tx = con.BeginTransaction();
            var affected = 0;

            var values = new (string Key, string Value)[]
            {
                ("language", Language),
                ("theme", Theme),
                ("auto_backup", AutomaticBackup ? "true" : "false"),
                ("integrity_check_enabled", IntegrityCheckEnabled ? "true" : "false")
            };

            foreach (var (key, value) in values)
            {
                using var cmd = con.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO app_settings (setting_key, setting_value, updated_at)
                    VALUES ($key, $value, $now)
                    ON CONFLICT(setting_key) DO UPDATE SET
                        setting_value = excluded.setting_value,
                        updated_at    = excluded.updated_at;
                    """;

                cmd.Parameters.AddWithValue("$key", key);
                cmd.Parameters.AddWithValue("$value", value);
                cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));

                affected += cmd.ExecuteNonQuery();
            }

            tx.Commit();
            return affected;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Settings", "settings could not be synced to the database: " + ex.Message);
            return 0;
        }
    }

    private static string ReadString(JsonElement root, string name, string fallback)
    {
        if (root.TryGetProperty(name, out var element) &&
            element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString();

            if (!string.IsNullOrWhiteSpace(value))
                return value!;
        }

        return fallback;
    }

    private static bool ReadBool(JsonElement root, string name, bool fallback)
    {
        if (root.TryGetProperty(name, out var element))
        {
            if (element.ValueKind == JsonValueKind.True)
                return true;

            if (element.ValueKind == JsonValueKind.False)
                return false;
        }

        return fallback;
    }
}
