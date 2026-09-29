// Phase 4: extracted from MainWindow.xaml.cs. Behaviour is unchanged;
// this file only groups one responsibility so the code stays maintainable.
using System.IO;
using System.Text.Json;

namespace Instageram;

public static class PortablePaths
{
    public static string Root { get; private set; } = "";
    public static string Config => Path.Combine(Root, "config");
    public static string Data => Path.Combine(Root, "data");
    public static string Assets => Path.Combine(Root, "assets");
    public static string Languages => Path.Combine(Assets, "languages");
    public static string Backups => Path.Combine(Data, "backups");
    public static string Reports => Path.Combine(Root, "reports");
    public static string Exports => Path.Combine(Root, "exports");
    public static string Logs => Path.Combine(Root, "logs");
    public static string Cache => Path.Combine(Root, "cache");
    // Phase 1 fix: was hard-coded to an absolute Debug path on drive E:.
    // Must stay relative to the executable so portable mode really works.
    public static string Database => Path.Combine(Data, "database.db");
    public static string Settings => Path.Combine(Config, "settings.json");
    public static string UsersFile => Path.Combine(Config, "instagram_users.txt");
    public static string LogFile => Path.Combine(Logs, "application.log");

    /// <summary>
    /// Phase 4: the shipped defaults file inside config\. When present it is
    /// copied to settings.json on first run, so a deployment can pre-configure
    /// the application. This replaces the old second settings.json at the
    /// project root, which the running application never read.
    /// </summary>
    public const string SettingsTemplateFileName = "settings.template.json";

    public static string SettingsTemplate => Path.Combine(Config, SettingsTemplateFileName);

    public static void Initialize()
    {
        Root = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

        foreach (var folder in new[] { Config, Data, Backups, Reports, Exports, Logs, Cache,
            Path.Combine(Data, "campaigns"), Path.Combine(Data, "projects"),
            Path.Combine(Root, "assets"), Path.Combine(Root, "assets", "icons"),
            Path.Combine(Root, "assets", "images"), Path.Combine(Root, "assets", "templates") })
        {
            Directory.CreateDirectory(folder);
        }

        MigrateLegacyDatabase();

        if (!File.Exists(Settings))
        {
            CreateDefaultSettings();
        }

        // Phase 3: the local profile list is created as a template on first run
        // so the user always has a file to edit (and the UI never points at a
        // path that does not exist).
        if (!File.Exists(UsersFile))
        {
            File.WriteAllText(UsersFile, """
                # INSTAGERAM - local Instagram profile list
                # One username per line. Lines starting with # are ignored.
                # Full profile URLs are accepted as well.
                #
                # Examples:
                # example
                # example.page
                # https://www.instagram.com/example/
                """);
        }
    }

    // Legacy absolute locations used before Phase 1.
    // Kept ONLY so existing user data can be migrated forward automatically.
    private static readonly string[] LegacyDatabasePaths =
    {
        @"E:\instageram\src\Instageram\bin\Debug\net8.0-windows\win-x64\data\database.db"
    };

    /// <summary>
    /// Phase 4: a new installation starts from config\settings.template.json
    /// when that file was shipped, so a deployment can pre-configure language,
    /// theme and backup behaviour. Without a template the built-in defaults are
    /// written, so the application never runs without a settings file.
    ///
    /// This removes the old confusion: the project used to carry a second
    /// settings.json that the running application never read.
    /// </summary>
    private static void CreateDefaultSettings()
    {
        var template = Path.Combine(Config, SettingsTemplateFileName);

        if (File.Exists(template))
        {
            try
            {
                var content = File.ReadAllText(template);

                // Only accept it when it really is a JSON object.
                if (JsonSerializer.Deserialize<JsonElement>(content).ValueKind == JsonValueKind.Object)
                {
                    File.WriteAllText(Settings, content);
                    AppLogger.Info("Settings", "settings.json created from " + template);

                    return;
                }

                AppLogger.Error("Settings", "settings template is not a JSON object; built-in defaults are used.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Settings", "settings template could not be used: " + ex.Message);
            }
        }

        var defaults = new
        {
            application_name = "INSTAGERAM",
            application_version = AppInfo.Version,
            language = "fa",
            theme = "Light",
            database_path = @"data\database.db",
            reports_path = "reports",
            exports_path = "exports",
            backup_path = @"data\backups",
            log_path = @"logs\application.log",
            auto_backup = true,
            integrity_check_enabled = true,
            startup_mode = "Dashboard",
            portable_mode = true
        };

        File.WriteAllText(Settings, JsonSerializer.Serialize(defaults, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    /// <summary>
    /// Copies an existing legacy database next to the executable when no
    /// portable database exists yet. Never overwrites an existing database.
    /// </summary>
    private static void MigrateLegacyDatabase()
    {
        try
        {
            if (File.Exists(Database))
                return;

            // Operational switch: setting INSTAGERAM_SKIP_LEGACY_MIGRATION=1
            // keeps the application from copying an old absolute-path database.
            // The automated tests rely on it to get a truly empty database.
            if (string.Equals(
                    Environment.GetEnvironmentVariable("INSTAGERAM_SKIP_LEGACY_MIGRATION"),
                    "1",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            foreach (var legacy in LegacyDatabasePaths)
            {
                if (!File.Exists(legacy))
                    continue;

                var targetDirectory = Path.GetDirectoryName(Database);

                if (!string.IsNullOrWhiteSpace(targetDirectory))
                    Directory.CreateDirectory(targetDirectory);

                File.Copy(legacy, Database, false);

                foreach (var suffix in new[] { "-wal", "-shm" })
                {
                    var side = legacy + suffix;

                    if (File.Exists(side))
                        File.Copy(side, Database + suffix, true);
                }

                AppLogger.Info("Migration", "Legacy database migrated from " + legacy);
                break;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Migration", ex.ToString());
        }
    }
}
