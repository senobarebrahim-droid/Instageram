using System.IO;
using System.Text.Json.Nodes;
using Xunit;

namespace Instageram.Tests;

public class AppSettingsTests
{
    public AppSettingsTests() => TestEnvironment.EnsureReady();

    [Fact]
    public void Defaults_are_used_when_the_file_is_missing()
    {
        WithSettingsFile(null, () =>
        {
            AppSettings.Load();

            Assert.False(string.IsNullOrWhiteSpace(AppSettings.Language));
            Assert.False(string.IsNullOrWhiteSpace(AppSettings.Theme));
            Assert.False(string.IsNullOrWhiteSpace(AppSettings.StartupMode));
        });
    }

    [Fact]
    public void A_broken_settings_file_does_not_stop_the_application()
    {
        WithSettingsFile("{ this is not valid json", () =>
        {
            AppSettings.Load();

            Assert.False(string.IsNullOrWhiteSpace(AppSettings.Language));
        });
    }

    [Fact]
    public void Saved_values_are_read_back()
    {
        WithSettingsFile("{}", () =>
        {
            AppSettings.Set("en", "Dark", false, false);
            Assert.True(AppSettings.Save());

            AppSettings.Load();

            Assert.Equal("en", AppSettings.Language);
            Assert.Equal("Dark", AppSettings.Theme);
            Assert.False(AppSettings.AutomaticBackup);
            Assert.False(AppSettings.IntegrityCheckEnabled);
        });
    }

    [Fact]
    public void Saving_preserves_keys_the_application_does_not_manage()
    {
        WithSettingsFile("{}", () =>
        {
            var node = JsonNode.Parse(File.ReadAllText(PortablePaths.Settings))!.AsObject();
            node["my_custom_note"] = "keep me";
            File.WriteAllText(PortablePaths.Settings, node.ToJsonString());

            AppSettings.Load();
            AppSettings.Set("fa", "Light", true, true);
            Assert.True(AppSettings.Save());

            var saved = JsonNode.Parse(File.ReadAllText(PortablePaths.Settings))!.AsObject();

            Assert.Equal("keep me", saved["my_custom_note"]!.GetValue<string>());
            Assert.Equal("Light", saved["theme"]!.GetValue<string>());
        });
    }

    [Fact]
    public void Saving_mirrors_the_values_into_the_app_settings_table()
    {
        WithSettingsFile("{}", () =>
        {
            AppSettings.Set("en", "Dark", true, false);
            Assert.True(AppSettings.Save());

            using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                {
                    DataSource = PortablePaths.Database,
                    Pooling = false
                }.ToString());

            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT setting_value FROM app_settings WHERE setting_key = 'theme';";

            Assert.Equal("Dark", command.ExecuteScalar()?.ToString());
        });
    }

    /// <summary>
    /// Runs the body against a known settings file and always puts the real one
    /// back, so tests never leak state into each other.
    /// </summary>
    private static void WithSettingsFile(string? content, Action body)
    {
        var original = File.Exists(PortablePaths.Settings)
            ? File.ReadAllText(PortablePaths.Settings)
            : null;

        try
        {
            if (content == null)
            {
                if (File.Exists(PortablePaths.Settings))
                    File.Delete(PortablePaths.Settings);
            }
            else
            {
                File.WriteAllText(PortablePaths.Settings, content);
            }

            body();
        }
        finally
        {
            if (original == null)
            {
                if (File.Exists(PortablePaths.Settings))
                    File.Delete(PortablePaths.Settings);
            }
            else
            {
                File.WriteAllText(PortablePaths.Settings, original);
            }

            AppSettings.Load();
        }
    }
}
