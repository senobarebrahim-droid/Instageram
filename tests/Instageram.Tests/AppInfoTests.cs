using System.IO;
using System.Text.Json;
using Xunit;

namespace Instageram.Tests;

public class AppInfoTests
{
    [Fact]
    public void Product_name_is_stable()
    {
        Assert.Equal("INSTAGERAM", AppInfo.ProductName);
    }

    [Fact]
    public void Version_is_readable_and_free_of_build_metadata()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.Version));
        Assert.DoesNotContain("+", AppInfo.Version);
        Assert.Matches(@"^\d+\.\d+\.\d+", AppInfo.Version);
    }

    [Fact]
    public void Summary_mentions_the_product_the_version_and_the_schema()
    {
        var summary = AppInfo.Summary();

        Assert.Contains(AppInfo.ProductName, summary);
        Assert.Contains(AppInfo.Version, summary);
        Assert.Contains("schema v" + SchemaMigrator.CurrentVersion, summary);
    }

    [Fact]
    public void Schema_version_matches_the_migrator()
    {
        Assert.Equal(SchemaMigrator.CurrentVersion, AppInfo.SchemaVersion);
    }

    [Fact]
    public void Assembly_version_matches_the_release_descriptor()
    {
        // Regression guard: bumping the project version without updating
        // manifest.json (or the other way round) used to be invisible.
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "manifest.json");

        Assert.True(File.Exists(path), "fixture not copied: " + path);

        using var document = JsonDocument.Parse(File.ReadAllText(path));

        var declared = document.RootElement.GetProperty("application_version").GetString();
        var schema = document.RootElement.GetProperty("database_schema_version").GetInt32();

        Assert.Equal(AppInfo.Version, declared);
        Assert.Equal(SchemaMigrator.CurrentVersion, schema);
    }

    [Fact]
    public void Report_descriptor_names_the_build_script()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "manifest.json");
        var note = JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("note").GetString() ?? "";

        Assert.Contains("build-release.ps1", note);
    }
}
