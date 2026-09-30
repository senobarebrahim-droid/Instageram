// Phase 4: extracted from MainWindow.xaml.cs. Behaviour is unchanged;
// this file only groups one responsibility so the code stays maintainable.
using Microsoft.Data.Sqlite;
using System.Data;
using System.IO;
using System.Text;

namespace Instageram;

public static class DatabaseService
{
    internal static string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = PortablePaths.Database,
        Mode = SqliteOpenMode.ReadWriteCreate
    }.ToString();

    /// <summary>Schema version that is actually on disk, filled by Initialize().</summary>
    public static int SchemaVersion { get; private set; }

    /// <summary>
    /// Phase 4: set when the last Initialize had to repair a damaged database,
    /// so the window can tell the user what happened.
    /// </summary>
    public static DatabaseHealth.RecoveryResult? LastRecovery { get; private set; }

    public static void Initialize()
    {
        LastRecovery = null;

        // Phase 4: a damaged file must never end the session. It is moved
        // aside and everything still readable is salvaged into a fresh
        // database. A missing or healthy database returns immediately.
        var recovery = DatabaseHealth.EnsureUsable(PortablePaths.Database);

        if (recovery.Recovered)
            LastRecovery = recovery;

        using var con = new SqliteConnection(ConnectionString);
        con.Open();

        // WAL mode cannot be switched inside a transaction, so it runs first.
        using (var pragma = con.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
            pragma.ExecuteNonQuery();
        }

        // Phase 2: versioned, additive migration (see Core\SchemaMigrator.cs).
        SchemaVersion = SchemaMigrator.Apply(con);
    }

    /// <summary>
    /// Phase 2: honours the settings file at startup. Both actions are
    /// best-effort and must never prevent the application from starting.
    /// </summary>
    public static void RunStartupMaintenance()
    {
        // Self-healing: anything still inside cache is debris from a run that
        // was interrupted (crash, power loss, locked file), so it is removed.
        FileTools.CleanDirectoryChildren(PortablePaths.Cache);

        // Self-healing: fold the write-ahead log back into the main database
        // file so that even a manual copy of database.db is complete.
        try
        {
            using var con = new SqliteConnection(ConnectionString);
            con.Open();

            using var checkpoint = con.CreateCommand();
            checkpoint.CommandTimeout = 5;
            checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            checkpoint.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Checkpoint", ex.Message);
        }

        if (AppSettings.IntegrityCheckEnabled)
        {
            try
            {
                using var con = new SqliteConnection(ConnectionString);
                con.Open();

                using var cmd = con.CreateCommand();
                cmd.CommandText = "PRAGMA integrity_check;";

                var result = cmd.ExecuteScalar()?.ToString() ?? "";

                if (string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                    AppLogger.Info("Integrity", "integrity_check = ok");
                else
                    AppLogger.Error("Integrity", "integrity_check reported: " + result);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Integrity", ex.Message);
            }
        }

        if (AppSettings.AutomaticBackup)
        {
            try
            {
                var prefix = "Instageram_Backup_" + DateTime.Now.ToString("yyyyMMdd");

                var alreadyDone = Directory.Exists(PortablePaths.Backups) &&
                    Directory.EnumerateFiles(PortablePaths.Backups, prefix + "*.zip").Any();

                if (!alreadyDone)
                    AppLogger.Info("Backup", "Automatic startup backup created: " + BackupService.Create());
            }
            catch (Exception ex)
            {
                AppLogger.Error("Backup", "Automatic startup backup failed: " + ex.Message);
            }
        }
    }

    public static long AddCampaign(string name, string username, string url, long target, long current, string countries, bool startNow)
    {
        using var con = new SqliteConnection(ConnectionString);
        con.Open();

        using var tx = con.BeginTransaction();

        try
        {
            var now = DateTime.UtcNow.ToString("O");
            long projectId;

            using (var project = con.CreateCommand())
            {
                project.Transaction = tx;
                project.CommandText = """
                INSERT INTO projects (project_name, instagram_username, instagram_url, created_at)
                VALUES ($name, $username, $url, $now);
                SELECT last_insert_rowid();
                """;

                project.Parameters.AddWithValue("$name", name);
                project.Parameters.AddWithValue("$username", username);
                project.Parameters.AddWithValue("$url", url);
                project.Parameters.AddWithValue("$now", now);

                projectId = Convert.ToInt64(project.ExecuteScalar());
            }

            long campaignId;

            using (var campaign = con.CreateCommand())
            {
                campaign.Transaction = tx;
                                campaign.CommandText = """
                INSERT INTO campaigns
                (project_id, campaign_name, countries, target_number, current_number, status, start_date, instagram_url)
                VALUES ($project, $name, $countries, $target, $current, $status, $now, $url);

                SELECT last_insert_rowid();
                """;

                campaign.Parameters.AddWithValue("$project", projectId);
                campaign.Parameters.AddWithValue("$name", name);
                campaign.Parameters.AddWithValue("$countries", countries);
                campaign.Parameters.AddWithValue("$target", target);
                campaign.Parameters.AddWithValue("$current", current);
                campaign.Parameters.AddWithValue("$status", startNow ? "Active" : "Draft");
                campaign.Parameters.AddWithValue("$now", now);
                campaign.Parameters.AddWithValue("$url", url);

                campaignId = Convert.ToInt64(campaign.ExecuteScalar());

            }

            tx.Commit();

            AppLogger.Info("Database", "Campaign created: " + campaignId);

            return campaignId;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }


    public static string GetCampaignUrl(long campaignId)
{
    using var con = new SqliteConnection(ConnectionString);
    con.Open();

    using var cmd = con.CreateCommand();

    cmd.CommandText = """
        SELECT 
            CASE 
                WHEN c.instagram_url IS NOT NULL AND c.instagram_url <> '' 
                THEN c.instagram_url
                ELSE p.instagram_url
            END
        FROM campaigns c
        LEFT JOIN projects p ON p.id = c.project_id
        WHERE c.id = $id
        LIMIT 1;
        """;

    cmd.Parameters.AddWithValue("$id", campaignId);

    var result = cmd.ExecuteScalar();

    if(result == null || result == DBNull.Value)
        return "";

    return result.ToString() ?? "";
}

public static long GetLatestCampaignId()
{
    using var con = new SqliteConnection(ConnectionString);
    con.Open();

    using var cmd = con.CreateCommand();

    cmd.CommandText = """
    SELECT id
    FROM campaigns
    ORDER BY id DESC
    LIMIT 1;
    """;

    var result = cmd.ExecuteScalar();

    if(result == null)
        return 0;

    return Convert.ToInt64(result);
}

public static long GetLatestProjectCampaignId()
{
    using var con = new SqliteConnection(ConnectionString);
    con.Open();

    using var cmd = con.CreateCommand();

    cmd.CommandText = """
    SELECT id
    FROM campaigns
    ORDER BY id DESC
    LIMIT 1;
    """;

    var result = cmd.ExecuteScalar();

    if(result == null || result == DBNull.Value)
        return 0;

    return Convert.ToInt64(result);
}
public static void RegisterCampaignActivity(long campaignId)
{
    RegisterCampaignCompletion(campaignId);
}

    public static void RegisterCampaignCompletion(long campaignId)
    {
        if(campaignId <= 0)
            return;

        using var con = new SqliteConnection(ConnectionString);
        con.Open();

        using var cmd = con.CreateCommand();

        cmd.CommandText = """
            UPDATE campaigns
            SET current_number =
                CASE
                    WHEN current_number < target_number
                    THEN current_number + 1
                    ELSE current_number
                END,
                status =
                CASE
                    WHEN current_number + 1 >= target_number
                    THEN 'Completed'
                    ELSE 'Active'
                END
            WHERE id = $id;
            """;

        cmd.Parameters.AddWithValue("$id", campaignId);
        cmd.ExecuteNonQuery();
    }
    public static long GetCampaignRemaining(long campaignId)
{
    if(campaignId <= 0)
        return 0;

    using var con = new SqliteConnection(ConnectionString);
    con.Open();

    using var cmd = con.CreateCommand();
    cmd.CommandText = """
        SELECT MAX(target_number - current_number, 0)
        FROM campaigns
        WHERE id = $id;
        """;

    cmd.Parameters.AddWithValue("$id", campaignId);

    var value = cmd.ExecuteScalar();

    if(value == null || value == DBNull.Value)
        return 0;

    return Convert.ToInt64(value);
}
public static Summary GetSummary()
    {
        using var con = new SqliteConnection(ConnectionString);
        con.Open();

        using var cmd = con.CreateCommand();

        cmd.CommandText = """
        SELECT
            (SELECT COUNT(*) FROM projects),
            (SELECT COUNT(*) FROM campaigns),
            COALESCE((SELECT SUM(target_number) FROM campaigns), 0),
            COALESCE((SELECT SUM(current_number) FROM campaigns), 0);
        """;

        using var reader = cmd.ExecuteReader();

        reader.Read();

        return new Summary
        {
            Projects = reader.GetInt64(0),
            Campaigns = reader.GetInt64(1),
            هدف = reader.GetInt64(2),
            Target = reader.GetInt64(2),
            Current = reader.GetInt64(3)
        };
    }

    public static DataTable GetCampaigns()
    {
        using var con = new SqliteConnection(ConnectionString);
        con.Open();

        using var cmd = con.CreateCommand();

        // Phase 3: the SQL alias is a stable technical name; the visible
        // header text is produced by LocalizeColumns below.
        cmd.CommandText = """
        SELECT
            c.id AS ID,
            c.campaign_name AS Campaign,
            p.instagram_username AS Username,
            c.countries AS Countries,
            c.target_number AS Target,
            c.current_number AS Current,
            ROUND(c.current_number * 100.0 / c.target_number, 1) AS ProgressPercent,
            c.status AS Status,
            c.start_date AS StartDate
        FROM campaigns c
        INNER JOIN projects p ON p.id = c.project_id
        ORDER BY c.id DESC;
        """;

        using var reader = cmd.ExecuteReader();

        var table = new DataTable();
        table.Load(reader);

        LocalizeColumns(table);

        return table;
    }

    /// <summary>
    /// Phase 3: replaces the technical column names with translated headers, so
    /// the dashboard grid, the CSV, the XLSX and the printable report all show
    /// the same human text in the selected language.
    /// </summary>
    private static void LocalizeColumns(DataTable table)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ID"] = "col_id",
            ["Campaign"] = "col_campaign",
            ["Username"] = "col_username",
            ["Countries"] = "col_countries",
            ["Target"] = "col_target",
            ["Current"] = "col_current",
            ["ProgressPercent"] = "col_progress",
            ["Status"] = "col_status",
            ["StartDate"] = "col_start_date",
            ["GeneratedDate"] = "col_generated",
            ["Date"] = "col_date",
            ["Followers"] = "col_followers",
            ["Growth"] = "col_growth",
            ["Engagement"] = "col_engagement",
            ["Notes"] = "col_notes",
            ["Records"] = "col_records"
        };

        foreach (DataColumn column in table.Columns)
        {
            if (map.TryGetValue(column.ColumnName, out var key))
                column.ColumnName = Localization.T(key);
        }
    }

    public static string ExportCsv()
    {
        var file = Path.Combine(
            PortablePaths.Exports,
            "Instageram_Report_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");

        var data = GetCampaigns();
        var builder = new StringBuilder();

        builder.AppendLine(
            string.Join(",", data.Columns.Cast<DataColumn>().Select(c => Quote(c.ColumnName))) +
            "," + Quote(Localization.T("col_generated")));

        foreach (DataRow row in data.Rows)
        {
            var values = row.ItemArray.Select(x => Quote(x?.ToString() ?? ""));

            builder.AppendLine(
                string.Join(",", values) +
                "," + Quote(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
        }

        File.WriteAllText(file, builder.ToString(), new UTF8Encoding(true));

        AppLogger.Info("Report", "CSV written: " + file);

        return file;
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\"", "\"\"") + "\"";

    /// <summary>Phase 3: Excel report (no third-party package needed).</summary>
    public static string ExportXlsx()
    {
        var file = Path.Combine(
            PortablePaths.Exports,
            "Instageram_Report_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx");

        return ReportWriter.WriteXlsx(file, GetCampaigns(), Localization.T("report_sheet_name"));
    }

    /// <summary>
    /// Phase 3: printable report. One self-contained HTML file that can be
    /// turned into a PDF with the browser's own "Print to PDF".
    /// </summary>
    public static string ExportHtml()
    {
        var file = Path.Combine(
            PortablePaths.Exports,
            "Instageram_Report_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".html");

        var summary = GetSummary();

        var information = new List<KeyValuePair<string, string>>
        {
            new(Localization.T("report_projects"), summary.Projects.ToString("N0")),
            new(Localization.T("report_campaigns"), summary.Campaigns.ToString("N0")),
            new(Localization.T("report_total_target"), summary.Target.ToString("N0")),
            new(Localization.T("report_total_current"), summary.Current.ToString("N0")),
            new(Localization.T("report_progress"), summary.Progress.ToString("N2") + "%"),
            new(Localization.T("report_schema"), "v" + SchemaVersion)
        };

        return ReportWriter.WriteHtml(
            file,
            Localization.T("report_title"),
            GetCampaigns(),
            information);
    }

    // ================= Phase 3: statistics storage =================

    /// <summary>One selectable campaign, for the statistics form.</summary>
    public sealed class CampaignOption
    {
        public long Id { get; init; }
        public long ProjectId { get; init; }
        public string Label { get; init; } = "";

        public override string ToString() => Label;
    }

    public static List<CampaignOption> GetCampaignOptions()
    {
        var options = new List<CampaignOption>();

        using var con = new SqliteConnection(ConnectionString);
        con.Open();

        using var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT c.id, c.project_id, c.campaign_name, p.instagram_username
            FROM campaigns c
            INNER JOIN projects p ON p.id = c.project_id
            ORDER BY c.id DESC;
            """;

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            var id = reader.GetInt64(0);
            var projectId = reader.GetInt64(1);
            var name = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var username = reader.IsDBNull(3) ? "" : reader.GetString(3);

            options.Add(new CampaignOption
            {
                Id = id,
                ProjectId = projectId,
                Label = $"#{id} · {name} (@{username})"
            });
        }

        return options;
    }

    /// <summary>
    /// Phase 3: stores one day of measurements for a campaign and its project.
    /// Both inserts share a single transaction, so a partial record can never
    /// be written.
    /// </summary>
    public static long RecordStatistics(
        long campaignId,
        long projectId,
        string date,
        long followers,
        long growth,
        double engagementRate,
        string country,
        string notes)
    {
        using var con = new SqliteConnection(ConnectionString);
        con.Open();

        using var tx = con.BeginTransaction();

        try
        {
            long statisticsId;

            using (var campaign = con.CreateCommand())
            {
                campaign.Transaction = tx;
                campaign.CommandText = """
                    INSERT INTO campaign_statistics
                        (campaign_id, date, followers, engagement_rate, growth, country, notes)
                    VALUES ($campaign, $date, $followers, $engagement, $growth, $country, $notes);

                    SELECT last_insert_rowid();
                    """;

                campaign.Parameters.AddWithValue("$campaign", campaignId);
                campaign.Parameters.AddWithValue("$date", date);
                campaign.Parameters.AddWithValue("$followers", followers);
                campaign.Parameters.AddWithValue("$engagement", engagementRate);
                campaign.Parameters.AddWithValue("$growth", growth);
                campaign.Parameters.AddWithValue("$country", country);
                campaign.Parameters.AddWithValue("$notes", notes);

                statisticsId = Convert.ToInt64(campaign.ExecuteScalar());
            }

            if (projectId > 0)
            {
                using var profile = con.CreateCommand();
                profile.Transaction = tx;
                profile.CommandText = """
                    INSERT INTO profile_statistics
                        (project_id, date, followers, following, post_count, engagement_rate, notes)
                    VALUES ($project, $date, $followers, 0, 0, $engagement, $notes);
                    """;

                profile.Parameters.AddWithValue("$project", projectId);
                profile.Parameters.AddWithValue("$date", date);
                profile.Parameters.AddWithValue("$followers", followers);
                profile.Parameters.AddWithValue("$engagement", engagementRate);
                profile.Parameters.AddWithValue("$notes", notes);

                profile.ExecuteNonQuery();
            }

            tx.Commit();

            AppLogger.Info(
                "Statistics",
                $"Recorded campaign={campaignId} project={projectId} date={date} followers={followers} growth={growth}");

            return statisticsId;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>Recent measurements, for the statistics grid and dashboard.</summary>
    public static DataTable GetRecentStatistics(int limit)
    {
        using var con = new SqliteConnection(ConnectionString);
        con.Open();

        using var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT
                s.id AS ID,
                s.date AS Date,
                c.campaign_name AS Campaign,
                s.followers AS Followers,
                s.growth AS Growth,
                s.engagement_rate AS Engagement,
                s.country AS Country,
                s.notes AS Notes
            FROM campaign_statistics s
            LEFT JOIN campaigns c ON c.id = s.campaign_id
            ORDER BY s.date DESC, s.id DESC
            LIMIT $limit;
            """;

        cmd.Parameters.AddWithValue("$limit", limit <= 0 ? 100 : limit);

        using var reader = cmd.ExecuteReader();

        var table = new DataTable();
        table.Load(reader);

        LocalizeColumns(table);

        return table;
    }

    /// <summary>Total followers recorded for a campaign, newest measurement.</summary>
    public static long GetLatestFollowers(long campaignId)
    {
        using var con = new SqliteConnection(ConnectionString);
        con.Open();

        using var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT followers
            FROM campaign_statistics
            WHERE campaign_id = $campaign
            ORDER BY date DESC, id DESC
            LIMIT 1;
            """;

        cmd.Parameters.AddWithValue("$campaign", campaignId);

        var value = cmd.ExecuteScalar();

        return value == null || value == DBNull.Value ? 0 : Convert.ToInt64(value);
    }

    // ================= Phase 3: growth analytics =================

    /// <summary>One point of the follower history.</summary>
    public sealed class GrowthPoint
    {
        public string Date { get; init; } = "";
        public long Followers { get; init; }
    }

    /// <summary>Head-line numbers for one campaign.</summary>
    public sealed class GrowthSummary
    {
        public long LatestFollowers { get; init; }
        public long TotalGrowth { get; init; }
        public double AverageEngagement { get; init; }
        public int Records { get; init; }
        public string FirstDate { get; init; } = "";
        public string LastDate { get; init; } = "";
    }

    /// <summary>
    /// Follower history in chronological order (oldest first), which is the
    /// order the chart draws.
    /// </summary>
    public static List<GrowthPoint> GetFollowerSeries(long campaignId, int limit)
    {
        var points = new List<GrowthPoint>();

        using var con = new SqliteConnection(ConnectionString);
        con.Open();

        using var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT date, followers
            FROM campaign_statistics
            WHERE campaign_id = $campaign
            ORDER BY date DESC, id DESC
            LIMIT $limit;
            """;

        cmd.Parameters.AddWithValue("$campaign", campaignId);
        cmd.Parameters.AddWithValue("$limit", limit <= 0 ? 90 : limit);

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            points.Add(new GrowthPoint
            {
                Date = reader.IsDBNull(0) ? "" : reader.GetString(0),
                Followers = reader.IsDBNull(1) ? 0 : reader.GetInt64(1)
            });
        }

        points.Reverse();

        return points;
    }

    public static GrowthSummary GetGrowthSummary(long campaignId)
    {
        using var con = new SqliteConnection(ConnectionString);
        con.Open();

        using var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT
                COALESCE(SUM(growth), 0),
                COALESCE(AVG(engagement_rate), 0),
                COUNT(*),
                COALESCE(MIN(date), ''),
                COALESCE(MAX(date), '')
            FROM campaign_statistics
            WHERE campaign_id = $campaign;
            """;

        cmd.Parameters.AddWithValue("$campaign", campaignId);

        using var reader = cmd.ExecuteReader();

        if (!reader.Read())
        {
            return new GrowthSummary
            {
                LatestFollowers = GetLatestFollowers(campaignId)
            };
        }

        return new GrowthSummary
        {
            LatestFollowers = GetLatestFollowers(campaignId),
            TotalGrowth = reader.IsDBNull(0) ? 0 : Convert.ToInt64(reader.GetValue(0)),
            AverageEngagement = reader.IsDBNull(1) ? 0 : Convert.ToDouble(reader.GetValue(1)),
            Records = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2)),
            FirstDate = reader.IsDBNull(3) ? "" : reader.GetString(3),
            LastDate = reader.IsDBNull(4) ? "" : reader.GetString(4)
        };
    }

    /// <summary>One row per campaign, so campaigns can be compared side by side.</summary>
    public static DataTable GetCampaignComparison()
    {
        using var con = new SqliteConnection(ConnectionString);
        con.Open();

        using var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT
                c.id AS ID,
                c.campaign_name AS Campaign,
                COUNT(s.id) AS Records,
                COALESCE(MAX(s.followers), 0) AS Followers,
                COALESCE(SUM(s.growth), 0) AS Growth,
                ROUND(COALESCE(AVG(s.engagement_rate), 0), 2) AS Engagement
            FROM campaigns c
            LEFT JOIN campaign_statistics s ON s.campaign_id = c.id
            GROUP BY c.id, c.campaign_name
            ORDER BY c.id DESC;
            """;

        using var reader = cmd.ExecuteReader();

        var table = new DataTable();
        table.Load(reader);

        LocalizeColumns(table);

        return table;
    }
}
