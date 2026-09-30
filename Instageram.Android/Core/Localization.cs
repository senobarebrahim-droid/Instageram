using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace Instageram;

/// <summary>
/// Phase 3: real localisation.
///
/// Strings live in assets\languages\{lang}.json. XAML refers to them with the
/// markup extension <c>{loc:Text key}</c>, which binds to the indexer below.
/// Because the binding is live, changing the language re-renders the whole
/// window immediately — no restart needed.
///
/// Resolution order: requested language file -> English file -> the raw key.
/// A missing or broken file can therefore never crash the application.
/// </summary>
public sealed class Localization : INotifyPropertyChanged
{
    public static Localization Instance { get; } = new();

    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public static string Language { get; private set; } = "fa";

    public static string Source { get; private set; } = "none";

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Indexer that the {loc:Text key} markup extension binds to.</summary>
    public string this[string key] => Get(key);

    public static string T(string key) => Instance.Get(key);

    /// <summary>
    /// Phase 3: formatted translation, e.g. "{0} profiles were loaded.".
    /// A template with a broken placeholder falls back to the raw text.
    /// </summary>
    public static string Format(string key, params object?[] args)
    {
        var template = T(key);

        try
        {
            return string.Format(template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    public string Get(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return "";

        return _values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : key;
    }

    public static void Load(string? language)
    {
        var normalized = string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)
            ? "en"
            : "fa";

        Instance._values.Clear();

        var loaded = TryLoadFile(normalized);

        if (!loaded && !string.Equals(normalized, "en", StringComparison.OrdinalIgnoreCase))
        {
            // Fall back to English rather than showing raw keys.
            loaded = TryLoadFile("en");
        }

        Source = loaded
            ? Path.Combine(PortablePaths.Languages, normalized + ".json")
            : "not found";

        Language = normalized;

        RaiseChanged();

        AppLogger.Info(
            "Language",
            $"Localisation loaded: language={normalized} strings={Instance._values.Count} source={Source}");
    }

    private static bool TryLoadFile(string language)
    {
        try
        {
            var file = Path.Combine(PortablePaths.Languages, language + ".json");

            if (!File.Exists(file))
                return false;

            using var document = JsonDocument.Parse(File.ReadAllText(file));

            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                    continue;

                var value = property.Value.GetString();

                if (!string.IsNullOrWhiteSpace(value))
                    Instance._values[property.Name] = value!;
            }

            return Instance._values.Count > 0;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Language", $"language file '{language}.json' could not be read: {ex.Message}");
            return false;
        }
    }

    private static void RaiseChanged() =>
        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs("Item[]"));
}

/// <summary>
/// XAML markup extension: <c>Text="{loc:Text campaign_title}"</c>.
/// Returns a one-way binding to <see cref="Localization"/>'s indexer, so the
/// text follows the current language automatically.
/// </summary>
public class TextExtension : MarkupExtension
{
    public string Key { get; set; } = "";

    public TextExtension()
    {
    }

    public TextExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding
        {
            Path = new PropertyPath("[" + Key + "]"),
            Source = Localization.Instance,
            Mode = BindingMode.OneWay
        };

        return binding.ProvideValue(serviceProvider);
    }
}
