using System.Windows;

namespace Instageram;

/// <summary>
/// Phase 3: applies the Light or Dark resource dictionary to the running
/// application.
///
/// Every colour in MainWindow.xaml is a DynamicResource key, so swapping the
/// dictionary updates the whole window immediately — no restart needed.
/// </summary>
public static class ThemeService
{
    public const string Light = "Light";
    public const string Dark = "Dark";

    private const string Marker = ";component/Themes/";

    public static string Current { get; private set; } = Light;

    public static string Normalize(string? theme) =>
        string.Equals(theme, Dark, StringComparison.OrdinalIgnoreCase) ? Dark : Light;

    /// <summary>Loads the requested theme. Returns false if it could not be applied.</summary>
    public static bool Apply(string? theme)
    {
        var normalized = Normalize(theme);

        try
        {
            var application = Application.Current;

            if (application == null)
            {
                // Headless modes have no Application resources to update.
                Current = normalized;
                return true;
            }

            var dictionaries = application.Resources.MergedDictionaries;

            var dictionary = new ResourceDictionary
            {
                Source = new Uri($"/Instageram;component/Themes/{normalized}.xaml", UriKind.Relative)
            };

            // Drop any previously applied theme dictionary, then add the new one.
            for (var i = dictionaries.Count - 1; i >= 0; i--)
            {
                var source = dictionaries[i].Source?.OriginalString ?? "";

                if (source.Contains(Marker, StringComparison.OrdinalIgnoreCase))
                    dictionaries.RemoveAt(i);
            }

            dictionaries.Insert(0, dictionary);

            Current = normalized;
            AppLogger.Info("Theme", "Theme applied: " + normalized);

            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Theme", "Theme could not be applied: " + ex);
            return false;
        }
    }
}
