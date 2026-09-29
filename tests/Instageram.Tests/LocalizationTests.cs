using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Instageram.Tests;

public class LocalizationTests
{
    public LocalizationTests() => TestEnvironment.EnsureReady();

    [Fact]
    public void Unknown_key_falls_back_to_the_key_itself()
    {
        Assert.Equal("this.key.does.not.exist", Localization.T("this.key.does.not.exist"));
    }

    [Fact]
    public void Format_replaces_the_placeholder()
    {
        var text = Localization.Format("import_done", 7);

        Assert.Contains("7", text);
        Assert.DoesNotContain("{0}", text);
    }

    [Fact]
    public void Format_returns_the_template_when_the_placeholder_is_broken()
    {
        var text = Localization.Format("growth_title", "unused");

        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [Fact]
    public void Both_language_files_expose_exactly_the_same_keys()
    {
        var fa = LoadKeys("fa");
        var en = LoadKeys("en");

        Assert.NotEmpty(fa);

        var onlyInFa = fa.Except(en).OrderBy(k => k).ToList();
        var onlyInEn = en.Except(fa).OrderBy(k => k).ToList();

        Assert.True(onlyInFa.Count == 0, "missing from en.json: " + string.Join(", ", onlyInFa));
        Assert.True(onlyInEn.Count == 0, "missing from fa.json: " + string.Join(", ", onlyInEn));
    }

    [Fact]
    public void Every_localisation_key_used_in_the_ui_exists_in_both_languages()
    {
        var xamlPath = Path.Combine(AppContext.BaseDirectory, "fixtures", "MainWindow.xaml");
        Assert.True(File.Exists(xamlPath), "fixture not copied: " + xamlPath);

        var xaml = File.ReadAllText(xamlPath);

        var used = Regex
            .Matches(xaml, @"\{loc:Text\s+([A-Za-z0-9_]+)\}")
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToList();

        Assert.NotEmpty(used);

        var fa = LoadKeys("fa");
        var en = LoadKeys("en");

        var missing = used
            .Where(key => !fa.Contains(key) || !en.Contains(key))
            .OrderBy(key => key)
            .ToList();

        Assert.True(missing.Count == 0, "keys missing from the language files: " + string.Join(", ", missing));
    }

    [Fact]
    public void No_language_value_is_empty()
    {
        foreach (var language in new[] { "fa", "en" })
        {
            var values = LoadValues(language);
            var empty = values.Where(pair => string.IsNullOrWhiteSpace(pair.Value)).Select(pair => pair.Key).ToList();

            Assert.True(empty.Count == 0, language + " has empty values: " + string.Join(", ", empty));
        }
    }

    [Fact]
    public void Switching_language_changes_the_visible_text()
    {
        Localization.Load("en");
        var english = Localization.T("growth_title");

        Localization.Load("fa");
        var persian = Localization.T("growth_title");

        Assert.NotEqual(english, persian);

        Localization.Load("en");
    }

    private static HashSet<string> LoadKeys(string language) =>
        LoadValues(language).Keys.ToHashSet(StringComparer.Ordinal);

    private static Dictionary<string, string> LoadValues(string language)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "assets", "languages", language + ".json");
        Assert.True(File.Exists(path), "language file not copied: " + path);

        using var document = JsonDocument.Parse(File.ReadAllText(path));

        return document.RootElement
            .EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(property => property.Name, property => property.Value.GetString() ?? "", StringComparer.Ordinal);
    }
}
