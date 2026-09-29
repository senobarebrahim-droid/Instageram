using System.Data;
using System.Globalization;
using System.IO;
using System.Windows;

namespace Instageram;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Phase 3: headless recovery path.
        //   Instageram.exe --restore <backup.zip>
        // Restores a backup without opening the UI (exit code 0 = success),
        // and makes the restore logic verifiable from the command line.
        if (e.Args.Length >= 2 &&
            string.Equals(e.Args[0], "--restore", StringComparison.OrdinalIgnoreCase))
        {
            RunHeadlessRestore(e.Args[1]);
            return;
        }

        // Phase 3: scriptable settings update (used by deployment scripts and
        // by the automated tests, exercising the same code as the Settings tab).
        //   Instageram.exe --settings-save <language> <theme> <autoBackup> <integrityCheck>
        if (e.Args.Length >= 5 &&
            string.Equals(e.Args[0], "--settings-save", StringComparison.OrdinalIgnoreCase))
        {
            RunHeadlessSettingsSave(e.Args[1], e.Args[2], e.Args[3], e.Args[4]);
            return;
        }

        // Phase 3: create a backup without the UI, so it can be scheduled and
        // verified automatically.  Instageram.exe --backup
        if (e.Args.Length >= 1 &&
            string.Equals(e.Args[0], "--backup", StringComparison.OrdinalIgnoreCase))
        {
            RunHeadlessBackup();
            return;
        }

        // Phase 3: show what the local profile list provides, without the UI.
        //   Instageram.exe --list-users
        if (e.Args.Length >= 1 &&
            string.Equals(e.Args[0], "--list-users", StringComparison.OrdinalIgnoreCase))
        {
            RunHeadlessListUsers();
            return;
        }

        // Phase 3: write a report without the UI, so exports can be scheduled.
        //   Instageram.exe --export <csv|xlsx|html>
        if (e.Args.Length >= 2 &&
            string.Equals(e.Args[0], "--export", StringComparison.OrdinalIgnoreCase))
        {
            RunHeadlessExport(e.Args[1]);
            return;
        }

        // Phase 3: record one measurement without the UI.
        //   Instageram.exe --stats-add <campaignId> <date> <followers> <growth> <engagement>
        if (e.Args.Length >= 6 &&
            string.Equals(e.Args[0], "--stats-add", StringComparison.OrdinalIgnoreCase))
        {
            RunHeadlessStatsAdd(e.Args[1], e.Args[2], e.Args[3], e.Args[4], e.Args[5]);
            return;
        }

        // Phase 3: dump the recorded statistics.
        //   Instageram.exe --stats
        if (e.Args.Length >= 1 &&
            string.Equals(e.Args[0], "--stats", StringComparison.OrdinalIgnoreCase))
        {
            RunHeadlessStatsDump();
            return;
        }

        // Phase 3: dump the growth analytics (chart series, summary, comparison).
        //   Instageram.exe --growth [campaignId]
        if (e.Args.Length >= 1 &&
            string.Equals(e.Args[0], "--growth", StringComparison.OrdinalIgnoreCase))
        {
            RunHeadlessGrowth(e.Args.Length >= 2 ? e.Args[1] : "");
            return;
        }

        // Phase 4: write a diagnostics report, so a user can send one file when
        // reporting a problem.  Instageram.exe --version
        if (e.Args.Length >= 1 &&
            string.Equals(e.Args[0], "--version", StringComparison.OrdinalIgnoreCase))
        {
            RunHeadlessVersion();
            return;
        }

        // Phase 4: repair a damaged database without opening the UI.
        //   Instageram.exe --recover
        if (e.Args.Length >= 1 &&
            string.Equals(e.Args[0], "--recover", StringComparison.OrdinalIgnoreCase))
        {
            RunHeadlessRecover();
            return;
        }

        try
        {
            PortablePaths.Initialize();

            // Phase 2: schema first, then configuration, then the log entry,
            // so the very first log line already has the real schema version.
            DatabaseService.Initialize();
            AppSettings.Load();
            CountryCatalog.Load();
            CountryCatalog.SyncToDatabase();
            AppSettings.SyncToDatabase();

            AppLogger.Info(
                "Application",
                $"{AppInfo.Summary()} started. " +
                $"countries={CountryCatalog.All.Count} (from_file={CountryCatalog.LoadedFromFile}) " +
                AppSettings.Summary());

            DatabaseService.RunStartupMaintenance();

            // Phase 3: the theme must exist before the window is created, so
            // every DynamicResource key resolves on the first load.
            ThemeService.Apply(AppSettings.Theme);

            // Phase 3: language strings must be loaded before the window is
            // created, so every {loc:Text ...} binding has a value.
            Localization.Load(AppSettings.Language);

            var window = new MainWindow();
            window.Show();
        }
        catch (Exception ex)
        {
            var logPath = WriteStartupError(ex);

            // The user gets one clear sentence; the stack trace stays in the log.
            if (Localization.Source == "none")
                Localization.Load(AppSettings.Language);

            MessageBox.Show(
                ErrorMessages.ForUser(ex) + Environment.NewLine + logPath,
                Localization.T("startup_error_title"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
        }
    }

    /// <summary>
    /// Phase 1 fix: startup failures are logged next to the executable
    /// instead of a hard-coded absolute path on drive E:.
    /// </summary>
    private static string WriteStartupError(Exception ex)
    {
        try
        {
            var logsDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logsDirectory);

            var file = Path.Combine(logsDirectory, "startup-error.log");

            File.AppendAllText(
                file,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | STARTUP FAILURE{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");

            return file;
        }
        catch
        {
            return "(startup log could not be written)";
        }
    }

    /// <summary>
    /// Phase 3: runs a restore without the UI, then exits with
    /// 0 = restored, 1 = restore failed, 2 = unexpected error.
    /// </summary>
    private void RunHeadlessRestore(string zipPath)
    {
        try
        {
            PortablePaths.Initialize();

            var result = BackupService.Restore(zipPath);

            WriteRestoreReport(zipPath, result);

            Shutdown(result.Success ? 0 : 1);
        }
        catch (Exception ex)
        {
            WriteStartupError(ex);
            Shutdown(2);
        }
    }

    /// <summary>Result file written by the headless restore path.</summary>
    private static void WriteRestoreReport(string zipPath, BackupService.RestoreResult result)
    {
        try
        {
            File.WriteAllText(
                Path.Combine(PortablePaths.Logs, "restore-last.log"),
                $"at={DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
                $"zip={zipPath}{Environment.NewLine}" +
                $"success={result.Success}{Environment.NewLine}" +
                $"safety_backup={result.SafetyBackup}{Environment.NewLine}" +
                $"schema_version={DatabaseService.SchemaVersion}{Environment.NewLine}" +
                $"message={result.Message}{Environment.NewLine}");
        }
        catch
        {
            // the restore already happened; reporting must not fail it
        }
    }

    /// <summary>
    /// Phase 3: updates settings without the UI.
    /// Exit codes: 0 = saved, 1 = save failed, 2 = unexpected error.
    /// </summary>
    private void RunHeadlessSettingsSave(
        string language,
        string theme,
        string autoBackup,
        string integrityCheck)
    {
        try
        {
            PortablePaths.Initialize();
            DatabaseService.Initialize();
            AppSettings.Load();

            AppSettings.Set(
                language,
                theme,
                ParseBool(autoBackup),
                ParseBool(integrityCheck));

            var saved = AppSettings.Save();

            WriteSettingsReport();

            Shutdown(saved ? 0 : 1);
        }
        catch (Exception ex)
        {
            WriteStartupError(ex);
            Shutdown(2);
        }
    }

    private static bool ParseBool(string value) =>
        value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("yes", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Phase 3: creates a backup without the UI, so it can be scheduled
    /// (Task Scheduler) and verified automatically.
    /// Exit codes: 0 = created, 1 = failed, 2 = unexpected error.
    /// </summary>
    private void RunHeadlessBackup()
    {
        try
        {
            PortablePaths.Initialize();
            DatabaseService.Initialize();

            var path = BackupService.Create();

            WriteBackupReport(path);

            Shutdown(string.IsNullOrWhiteSpace(path) ? 1 : 0);
        }
        catch (Exception ex)
        {
            WriteStartupError(ex);
            Shutdown(2);
        }
    }

    /// <summary>
    /// Phase 3: reports which profiles the local list file provides, without
    /// opening the UI. Exit codes: 0 = list has entries, 1 = nothing usable,
    /// 2 = unexpected error.
    /// </summary>
    private void RunHeadlessListUsers()
    {
        try
        {
            PortablePaths.Initialize();

            var provider = InstagramDiscoveryProviderFactory.Create();

            // Task.Run keeps the async file read off the dispatcher thread, so
            // waiting for it here can never deadlock.
            var candidates = Task.Run(() => provider.DiscoverAsync(10000))
                .GetAwaiter()
                .GetResult();

            var file = (provider as ImportedInstagramDiscoveryProvider)?.FilePath
                       ?? PortablePaths.UsersFile;

            WriteUsersReport(file, candidates);

            Shutdown(candidates.Count > 0 ? 0 : 1);
        }
        catch (Exception ex)
        {
            WriteStartupError(ex);
            Shutdown(2);
        }
    }

    /// <summary>Result file written by the headless list-users path.</summary>
    private static void WriteUsersReport(
        string file,
        IReadOnlyList<InstagramDiscoveryCandidate> candidates)
    {
        try
        {
            var builder = new System.Text.StringBuilder();

            builder.AppendLine("file=" + file);
            builder.AppendLine("count=" + candidates.Count);

            foreach (var candidate in candidates)
                builder.AppendLine(candidate.Username + " | " + candidate.ProfileUrl);

            File.WriteAllText(
                Path.Combine(PortablePaths.Logs, "users-last.log"),
                builder.ToString());
        }
        catch
        {
            // reporting must never fail the operation
        }
    }

    /// <summary>
    /// Phase 3: writes a report without opening the UI.
    /// Exit codes: 0 = written, 1 = unknown format or failure, 2 = crash.
    /// </summary>
    private void RunHeadlessExport(string format)
    {
        try
        {
            PortablePaths.Initialize();
            DatabaseService.Initialize();
            AppSettings.Load();
            Localization.Load(AppSettings.Language);

            var requested = (format ?? "").Trim().ToLowerInvariant();

            var path = requested switch
            {
                "xlsx" or "excel" => DatabaseService.ExportXlsx(),
                "html" or "print" => DatabaseService.ExportHtml(),
                "csv" => DatabaseService.ExportCsv(),
                _ => ""
            };

            WriteExportReport(requested, path);

            Shutdown(string.IsNullOrWhiteSpace(path) ? 1 : 0);
        }
        catch (Exception ex)
        {
            WriteStartupError(ex);
            Shutdown(2);
        }
    }

    /// <summary>Result file written by the headless export path.</summary>
    private static void WriteExportReport(string format, string path)
    {
        try
        {
            File.WriteAllText(
                Path.Combine(PortablePaths.Logs, "export-last.log"),
                $"at={DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
                $"format={format}{Environment.NewLine}" +
                $"path={path}{Environment.NewLine}" +
                $"language={Localization.Language}{Environment.NewLine}");
        }
        catch
        {
            // reporting must never fail the operation
        }
    }

    /// <summary>
    /// Phase 3: records one measurement without the UI, so the statistics
    /// tables can be filled by a scheduled task.
    /// Exit codes: 0 = stored, 1 = bad arguments, 2 = unexpected error.
    /// </summary>
    private void RunHeadlessStatsAdd(
        string campaignText,
        string date,
        string followersText,
        string growthText,
        string engagementText)
    {
        try
        {
            PortablePaths.Initialize();
            DatabaseService.Initialize();

            // Pre-declared so every out-parameter is definitely assigned even
            // when the chained && below short-circuits.
            long campaignId = 0;
            long followers = 0;
            long growth = 0;
            double engagement = 0;

            var parsed =
                long.TryParse(campaignText, out campaignId) &&
                long.TryParse(followersText, out followers) &&
                long.TryParse(growthText, out growth) &&
                double.TryParse(
                    engagementText.Replace(",", "."),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out engagement);

            if (!parsed)
            {
                WriteStatsAddReport("bad-arguments", 0);
                Shutdown(1);
                return;
            }

            var projectId = DatabaseService.GetCampaignOptions()
                .FirstOrDefault(option => option.Id == campaignId)?.ProjectId ?? 0;

            var id = DatabaseService.RecordStatistics(
                campaignId, projectId, date, followers, growth, engagement, "", "headless");

            WriteStatsAddReport("stored", id);

            Shutdown(id > 0 ? 0 : 1);
        }
        catch (Exception ex)
        {
            WriteStartupError(ex);
            Shutdown(2);
        }
    }

    /// <summary>Result file written by the headless stats-add path.</summary>
    private static void WriteStatsAddReport(string status, long id)
    {
        try
        {
            File.WriteAllText(
                Path.Combine(PortablePaths.Logs, "stats-add-last.log"),
                $"at={DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
                $"status={status}{Environment.NewLine}" +
                $"id={id}{Environment.NewLine}");
        }
        catch
        {
            // reporting must never fail the operation
        }
    }

    /// <summary>Phase 3: dumps the recorded statistics to a text file.</summary>
    private void RunHeadlessStatsDump()
    {
        try
        {
            PortablePaths.Initialize();
            DatabaseService.Initialize();
            AppSettings.Load();
            Localization.Load(AppSettings.Language);

            var table = DatabaseService.GetRecentStatistics(500);
            var builder = new System.Text.StringBuilder();

            builder.AppendLine("rows=" + table.Rows.Count);

            foreach (DataColumn column in table.Columns)
                builder.Append(column.ColumnName + " | ");

            builder.AppendLine();

            foreach (DataRow row in table.Rows)
            {
                foreach (var value in row.ItemArray)
                    builder.Append((value?.ToString() ?? "") + " | ");

                builder.AppendLine();
            }

            File.WriteAllText(
                Path.Combine(PortablePaths.Logs, "stats-last.log"),
                builder.ToString());

            Shutdown(0);
        }
        catch (Exception ex)
        {
            WriteStartupError(ex);
            Shutdown(2);
        }
    }

    /// <summary>
    /// Phase 3: dumps the growth analytics (series, summary, comparison)
    /// without opening the UI, so the numbers can be verified.
    /// Exit codes: 0 = written, 2 = unexpected error.
    /// </summary>
    private void RunHeadlessGrowth(string campaignText)
    {
        try
        {
            PortablePaths.Initialize();
            DatabaseService.Initialize();
            AppSettings.Load();
            Localization.Load(AppSettings.Language);

            var options = DatabaseService.GetCampaignOptions();

            var selected = long.TryParse(campaignText, out var requested) && requested > 0
                ? options.Where(option => option.Id == requested).ToList()
                : options;

            var builder = new System.Text.StringBuilder();

            foreach (var option in selected)
            {
                var series = DatabaseService.GetFollowerSeries(option.Id, 180);
                var summary = DatabaseService.GetGrowthSummary(option.Id);

                builder.AppendLine("campaign=" + option.Id + " label=" + option.Label);
                builder.AppendLine(
                    $"  records={summary.Records} latest={summary.LatestFollowers} " +
                    $"total_growth={summary.TotalGrowth} avg_engagement={summary.AverageEngagement:0.##}");
                builder.AppendLine($"  range={summary.FirstDate}..{summary.LastDate}");

                foreach (var point in series)
                    builder.AppendLine($"    {point.Date} = {point.Followers}");

                builder.AppendLine();
            }

            var comparison = DatabaseService.GetCampaignComparison();

            builder.AppendLine("comparison rows=" + comparison.Rows.Count);

            foreach (DataColumn column in comparison.Columns)
                builder.Append(column.ColumnName + " | ");

            builder.AppendLine();

            foreach (DataRow row in comparison.Rows)
            {
                foreach (var value in row.ItemArray)
                    builder.Append((value?.ToString() ?? "") + " | ");

                builder.AppendLine();
            }

            File.WriteAllText(
                Path.Combine(PortablePaths.Logs, "growth-last.log"),
                builder.ToString());

            Shutdown(0);
        }
        catch (Exception ex)
        {
            WriteStartupError(ex);
            Shutdown(2);
        }
    }

    /// <summary>
    /// Phase 4: writes logs\diagnostics.txt with the facts needed to diagnose a
    /// problem, without exposing any user content.
    /// Exit codes: 0 = written, 2 = unexpected error.
    /// </summary>
    private void RunHeadlessVersion()
    {
        try
        {
            PortablePaths.Initialize();
            DatabaseService.Initialize();
            AppSettings.Load();
            Localization.Load(AppSettings.Language);
            CountryCatalog.Load();

            var builder = new System.Text.StringBuilder();

            builder.AppendLine("product=" + AppInfo.ProductName);
            builder.AppendLine("version=" + AppInfo.Version);
            builder.AppendLine("schema_version=" + DatabaseService.SchemaVersion);
            builder.AppendLine("framework=" + AppInfo.Framework);
            builder.AppendLine("os=" + AppInfo.OperatingSystem);
            builder.AppendLine("portable_root=" + PortablePaths.Root);
            builder.AppendLine("database=" + PortablePaths.Database);
            builder.AppendLine("database_exists=" + File.Exists(PortablePaths.Database));
            builder.AppendLine("language=" + Localization.Language);
            builder.AppendLine("language_source=" + Localization.Source);
            builder.AppendLine("theme=" + AppSettings.Theme);
            builder.AppendLine("startup_mode=" + AppSettings.StartupMode);
            builder.AppendLine("auto_backup=" + AppSettings.AutomaticBackup);
            builder.AppendLine("integrity_check=" + AppSettings.IntegrityCheckEnabled);
            builder.AppendLine("countries=" + CountryCatalog.All.Count);
            builder.AppendLine("country_source_file=" + CountryCatalog.LoadedFromFile);
            builder.AppendLine("user=" + Environment.UserName);
            builder.AppendLine("machine=" + Environment.MachineName);

            File.WriteAllText(
                Path.Combine(PortablePaths.Logs, "diagnostics.txt"),
                builder.ToString());

            Shutdown(0);
        }
        catch (Exception ex)
        {
            WriteStartupError(ex);
            Shutdown(2);
        }
    }

    /// <summary>
    /// Phase 4: repairs a damaged database and reports what could be salvaged.
    /// Exit codes: 0 = usable (healthy or repaired), 1 = still unusable.
    /// </summary>
    private void RunHeadlessRecover()
    {
        try
        {
            PortablePaths.Initialize();
            AppSettings.Load();
            Localization.Load(AppSettings.Language);

            var result = DatabaseHealth.EnsureUsable(PortablePaths.Database);

            var state = DatabaseHealth.Inspect(PortablePaths.Database, out var problem);
            var healthy = state == DatabaseHealth.DatabaseState.Healthy;

            var builder = new System.Text.StringBuilder();

            builder.AppendLine("database=" + PortablePaths.Database);
            builder.AppendLine("file_exists=" + File.Exists(PortablePaths.Database));
            builder.AppendLine("file_size=" + (File.Exists(PortablePaths.Database) ? new FileInfo(PortablePaths.Database).Length : 0));
            builder.AppendLine("header_hex=" + DatabaseHealth.HeaderHex(PortablePaths.Database));
            builder.AppendLine("state=" + state);
            builder.AppendLine("was_recovered=" + result.Recovered);
            builder.AppendLine("quarantined=" + result.Quarantined);
            builder.AppendLine("quarantine_path=" + (result.QuarantinePath ?? ""));
            builder.AppendLine("salvaged_tables=" + result.SalvagedTables);
            builder.AppendLine("salvaged_rows=" + result.SalvagedRows);
            builder.AppendLine("healthy_now=" + healthy);
            builder.AppendLine("remaining_problem=" + problem);
            builder.AppendLine("message=" + result.Message);

            File.WriteAllText(
                Path.Combine(PortablePaths.Logs, "recovery-last.log"),
                builder.ToString());

            Shutdown(healthy ? 0 : 1);
        }
        catch (Exception ex)
        {
            WriteStartupError(ex);
            Shutdown(2);
        }
    }

    /// <summary>Result file written by the headless backup path.</summary>
    private static void WriteBackupReport(string path)
    {
        try
        {            File.WriteAllText(
                Path.Combine(PortablePaths.Logs, "backup-last.log"),
                $"at={DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
                $"zip={path}{Environment.NewLine}" +
                $"schema_version={DatabaseService.SchemaVersion}{Environment.NewLine}");
        }
        catch
        {
            // reporting must never fail the operation
        }
    }

    /// <summary>Result file written by the headless settings path.</summary>
    private static void WriteSettingsReport()
    {
        try
        {
            File.WriteAllText(
                Path.Combine(PortablePaths.Logs, "settings-last.log"),
                $"at={DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
                AppSettings.Summary() + Environment.NewLine +
                $"settings_file={PortablePaths.Settings}{Environment.NewLine}" +
                $"database={PortablePaths.Database}{Environment.NewLine}");
        }
        catch
        {
            // reporting must never fail the operation
        }
    }
}
