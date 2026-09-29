using System.IO;
using System.Text.Json.Nodes;
using Xunit;

namespace Instageram.Tests;

/// <summary>
/// Phase 4: the project used to carry a second settings.json that the running
/// application never read, which made people edit the wrong file. There is now
/// exactly one runtime file (config\settings.json, created next to the
/// executable) and one shipped defaults template that seeds it.
/// </summary>
public class SettingsTemplateTests
{
    public SettingsTemplateTests() => TestEnvironment.EnsureReady();

    [Fact]
    public void The_template_ships_next_to_the_executable()
    {
        Assert.True(
            File.Exists(PortablePaths.SettingsTemplate),
            "the defaults template was not copied: " + PortablePaths.SettingsTemplate);
    }

    [Fact]
    public void The_template_is_valid_json_with_every_managed_key()
    {
        var node = JsonNode.Parse(File.ReadAllText(PortablePaths.SettingsTemplate))!.AsObject();

        foreach (var key in new[]
                 {
                     "language", "theme", "auto_backup", "integrity_check_enabled", "startup_mode", "portable_mode"
                 })
        {
            Assert.True(node.ContainsKey(key), "the template is missing the key: " + key);
        }
    }

    [Fact]
    public void The_template_version_matches_the_application_version()
    {
        var node = JsonNode.Parse(File.ReadAllText(PortablePaths.SettingsTemplate))!.AsObject();

        Assert.Equal(AppInfo.Version, node["application_version"]!.GetValue<string>());
    }

    [Fact]
    public void A_missing_settings_file_is_recreated_from_the_template()
    {
        var settingsPath = PortablePaths.Settings;
        var original = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null;

        try
        {
            File.Delete(settingsPath);
            Assert.False(File.Exists(settingsPath));

            PortablePaths.Initialize();

            Assert.True(File.Exists(settingsPath), "settings.json was not recreated");

            var created = JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject();
            var template = JsonNode.Parse(File.ReadAllText(PortablePaths.SettingsTemplate))!.AsObject();

            Assert.Equal(template["language"]!.GetValue<string>(), created["language"]!.GetValue<string>());
            Assert.Equal(template["theme"]!.GetValue<string>(), created["theme"]!.GetValue<string>());
            Assert.Equal(template["startup_mode"]!.GetValue<string>(), created["startup_mode"]!.GetValue<string>());
        }
        finally
        {
            if (original != null)
                File.WriteAllText(settingsPath, original);

            AppSettings.Load();
        }
    }

    [Fact]
    public void An_existing_settings_file_is_never_overwritten_by_initialize()
    {
        var settingsPath = PortablePaths.Settings;

        File.WriteAllText(settingsPath, "{ \"language\": \"en\", \"theme\": \"Dark\" }");

        try
        {
            PortablePaths.Initialize();

            var node = JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject();

            Assert.Equal("en", node["language"]!.GetValue<string>());
            Assert.Equal("Dark", node["theme"]!.GetValue<string>());
        }
        finally
        {
            AppSettings.Set("fa", "Light", true, true);
            AppSettings.Save();
        }
    }
}
