using System.Data;
using System.IO;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Instageram.Tests;

public class DatabaseServiceTests
{
    public DatabaseServiceTests() => TestEnvironment.EnsureReady();

    [Fact]
    public void AddCampaign_creates_a_project_and_a_campaign()
    {
        var before = DatabaseService.GetSummary();

        var name = TestEnvironment.Unique("Campaign");
        var username = TestEnvironment.Unique("user");

        var id = DatabaseService.AddCampaign(
            name,
            username,
            $"https://www.instagram.com/{username}/",
            1000,
            0,
            "Iran",
            false);

        Assert.True(id > 0);

        var after = DatabaseService.GetSummary();

        Assert.Equal(before.Campaigns + 1, after.Campaigns);
        Assert.Equal(before.Projects + 1, after.Projects);
        Assert.Equal(before.Target + 1000, after.Target);
    }

    [Fact]
    public void Campaigns_can_be_read_back_with_the_stored_values()
    {
        var name = TestEnvironment.Unique("Readback");
        var username = TestEnvironment.Unique("user");

        var id = DatabaseService.AddCampaign(
            name, username, $"https://www.instagram.com/{username}/", 4321, 21, "Iran | Germany", true);

        var table = DatabaseService.GetCampaigns();
        var row = table.Rows.Cast<DataRow>().FirstOrDefault(r => Convert.ToInt64(r[0]) == id);

        Assert.NotNull(row);
        Assert.Equal(name, row![1]?.ToString());
        Assert.Equal(username, row[2]?.ToString());
        Assert.Equal("Iran | Germany", row[3]?.ToString());
        Assert.Equal(4321L, Convert.ToInt64(row[4]));
        Assert.Equal(21L, Convert.ToInt64(row[5]));
        Assert.Equal("Active", row[7]?.ToString());
    }

    [Fact]
    public void GetCampaignOptions_returns_the_project_for_each_campaign()
    {
        var username = TestEnvironment.Unique("user");

        var id = DatabaseService.AddCampaign(
            TestEnvironment.Unique("Options"), username, $"https://www.instagram.com/{username}/", 10, 0, "Iran", false);

        var option = DatabaseService.GetCampaignOptions().FirstOrDefault(o => o.Id == id);

        Assert.NotNull(option);
        Assert.True(option!.ProjectId > 0);
        Assert.Contains(username, option.Label);
    }

    [Fact]
    public void RecordStatistics_writes_to_both_tables_in_one_transaction()
    {
        var username = TestEnvironment.Unique("stats");
        var id = DatabaseService.AddCampaign(
            TestEnvironment.Unique("Stats"), username, $"https://www.instagram.com/{username}/", 100, 0, "Iran", false);

        var option = DatabaseService.GetCampaignOptions().First(o => o.Id == id);
        var date = "2026-03-0" + (DateTime.Now.Second % 9 + 1);

        var campaignRowsBefore = CountRows("campaign_statistics");
        var profileRowsBefore = CountRows("profile_statistics");

        var statisticsId = DatabaseService.RecordStatistics(
            id, option.ProjectId, date, 1234, 56, 2.5, "Iran", "unit test");

        Assert.True(statisticsId > 0);
        Assert.Equal(campaignRowsBefore + 1, CountRows("campaign_statistics"));
        Assert.Equal(profileRowsBefore + 1, CountRows("profile_statistics"));
    }

    [Fact]
    public void Growth_summary_reflects_the_recorded_measurements()
    {
        var username = TestEnvironment.Unique("growth");
        var id = DatabaseService.AddCampaign(
            TestEnvironment.Unique("Growth"), username, $"https://www.instagram.com/{username}/", 100, 0, "Iran", false);

        var option = DatabaseService.GetCampaignOptions().First(o => o.Id == id);

        DatabaseService.RecordStatistics(id, option.ProjectId, "2026-04-01", 1000, 100, 2.0, "", "");
        DatabaseService.RecordStatistics(id, option.ProjectId, "2026-04-02", 1200, 200, 3.0, "", "");
        DatabaseService.RecordStatistics(id, option.ProjectId, "2026-04-03", 1500, 300, 4.0, "", "");

        var summary = DatabaseService.GetGrowthSummary(id);

        Assert.Equal(3, summary.Records);
        Assert.Equal(1500L, summary.LatestFollowers);
        Assert.Equal(600L, summary.TotalGrowth);
        Assert.Equal(3.0, summary.AverageEngagement, 2);
        Assert.Equal("2026-04-01", summary.FirstDate);
        Assert.Equal("2026-04-03", summary.LastDate);
    }

    [Fact]
    public void Follower_series_is_returned_in_chronological_order()
    {
        var username = TestEnvironment.Unique("series");
        var id = DatabaseService.AddCampaign(
            TestEnvironment.Unique("Series"), username, $"https://www.instagram.com/{username}/", 100, 0, "Iran", false);

        var option = DatabaseService.GetCampaignOptions().First(o => o.Id == id);

        // deliberately written newest first
        DatabaseService.RecordStatistics(id, option.ProjectId, "2026-05-03", 300, 30, 1, "", "");
        DatabaseService.RecordStatistics(id, option.ProjectId, "2026-05-01", 100, 10, 1, "", "");
        DatabaseService.RecordStatistics(id, option.ProjectId, "2026-05-02", 200, 20, 1, "", "");

        var series = DatabaseService.GetFollowerSeries(id, 90);

        Assert.Equal(3, series.Count);
        Assert.Equal(new[] { "2026-05-01", "2026-05-02", "2026-05-03" }, series.Select(p => p.Date).ToArray());
        Assert.Equal(new long[] { 100, 200, 300 }, series.Select(p => p.Followers).ToArray());
    }

    [Fact]
    public void Campaign_comparison_returns_one_row_per_campaign()
    {
        var before = CountRows("campaigns");
        var table = DatabaseService.GetCampaignComparison();

        Assert.Equal(before, table.Rows.Count);
    }

    [Fact]
    public void Exports_are_written_and_reported()
    {
        var csv = DatabaseService.ExportCsv();
        var xlsx = DatabaseService.ExportXlsx();
        var html = DatabaseService.ExportHtml();

        foreach (var path in new[] { csv, xlsx, html })
        {
            Assert.True(File.Exists(path), "export missing: " + path);
            Assert.True(new FileInfo(path).Length > 0, "export is empty: " + path);
        }
    }

    private static long CountRows(string table)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = PortablePaths.Database,
            Pooling = false
        }.ToString());

        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM " + table;

        return Convert.ToInt64(command.ExecuteScalar() ?? 0L);
    }
}
