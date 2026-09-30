using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace Instageram;

/// <summary>One country / market entry.</summary>
public sealed class CountryEntry
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("name_fa")]
    public string NameFa { get; set; } = "";

    [JsonPropertyName("name_en")]
    public string NameEn { get; set; } = "";

    [JsonPropertyName("is_target_market")]
    public bool IsTargetMarket { get; set; }

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 999;

    /// <summary>Same format the UI has always used, e.g. "Iran (IR)".</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(NameEn) ? Code : $"{NameEn} ({Code})";
}

/// <summary>
/// Phase 2: the country list is no longer hard-coded inside MainWindow.
///
/// data\countries.json is now the source of truth. The small built-in list is
/// kept only as a safety net, so the UI can never end up empty if the JSON
/// file is missing or malformed. Entries from the file override or extend the
/// built-in ones, matched by country code.
/// </summary>
public static class CountryCatalog
{
    private static readonly CountryEntry[] Defaults =
    {
        new() { Code = "IR", NameFa = "Ø§ÛŒØ±Ø§Ù†",             NameEn = "Iran",                  IsTargetMarket = true, Priority = 1 },
        new() { Code = "US", NameFa = "Ø§ÛŒØ§Ù„Ø§Øª Ù…ØªØ­Ø¯Ù‡",      NameEn = "United States",         Priority = 2 },
        new() { Code = "DE", NameFa = "Ø¢Ù„Ù…Ø§Ù†",             NameEn = "Germany",               Priority = 3 },
        new() { Code = "FR", NameFa = "ÙØ±Ø§Ù†Ø³Ù‡",            NameEn = "France",                Priority = 4 },
        new() { Code = "TR", NameFa = "ØªØ±Ú©ÛŒÙ‡",             NameEn = "Turkey",                Priority = 5 },
        new() { Code = "GB", NameFa = "Ø¨Ø±ÛŒØªØ§Ù†ÛŒØ§",          NameEn = "United Kingdom",        Priority = 6 },
        new() { Code = "CA", NameFa = "Ú©Ø§Ù†Ø§Ø¯Ø§",            NameEn = "Canada",                Priority = 7 },
        new() { Code = "AE", NameFa = "Ø§Ù…Ø§Ø±Ø§Øª",            NameEn = "United Arab Emirates",  Priority = 8 },
        new() { Code = "ID", NameFa = "Ø§Ù†Ø¯ÙˆÙ†Ø²ÛŒ",           NameEn = "Indonesia",             Priority = 9 },
        new() { Code = "IN", NameFa = "Ù‡Ù†Ø¯",               NameEn = "India",                 Priority = 10 },
        new() { Code = "AU", NameFa = "Ø§Ø³ØªØ±Ø§Ù„ÛŒØ§",          NameEn = "Australia",             Priority = 11 }
    };

    private static IReadOnlyList<CountryEntry> _all = Sort(Defaults);

    public static IReadOnlyList<CountryEntry> All => _all;

    /// <summary>Strings for the existing UI list, e.g. "Iran (IR)".</summary>
    public static IReadOnlyList<string> DisplayNames =>
        _all.Select(c => c.DisplayName).ToList();

    /// <summary>True when the last load used data\countries.json.</summary>
    public static bool LoadedFromFile { get; private set; }

    public static void Load()
    {
        try
        {
            var file = Path.Combine(PortablePaths.Data, "countries.json");

            if (!File.Exists(file))
            {
                _all = Sort(Defaults);
                LoadedFromFile = false;
                return;
            }

            var fromFile = JsonSerializer.Deserialize<List<CountryEntry>>(
                File.ReadAllText(file),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new List<CountryEntry>();

            var merged = new Dictionary<string, CountryEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in Defaults)
            {
                if (!string.IsNullOrWhiteSpace(entry.Code))
                    merged[entry.Code] = entry;
            }

            foreach (var entry in fromFile)
            {
                if (string.IsNullOrWhiteSpace(entry.Code))
                    continue;

                merged[entry.Code] = Normalize(entry, merged.TryGetValue(entry.Code, out var known) ? known : null);
            }

            _all = Sort(merged.Values);
            LoadedFromFile = true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Countries", "countries.json could not be read; built-in list is used: " + ex.Message);

            _all = Sort(Defaults);
            LoadedFromFile = false;
        }
    }

    /// <summary>
    /// Phase 2: mirrors the catalogue into the countries table so the table
    /// holds real data and campaign_countries can reference it later.
    /// Idempotent: repeated runs only refresh the existing rows.
    /// </summary>
    public static int SyncToDatabase()
    {
        try
        {
            if (!File.Exists(PortablePaths.Database))
                return 0;

            using var con = new SqliteConnection(DatabaseService.ConnectionString);
            con.Open();

            using var tx = con.BeginTransaction();
            var affected = 0;

            foreach (var country in _all)
            {
                using var cmd = con.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO countries
                        (country_code, country_name, name_fa, is_target_market, priority, enabled, updated_at)
                    VALUES ($code, $name, $nameFa, $target, $priority, 1, $now)
                    ON CONFLICT(country_code) DO UPDATE SET
                        country_name     = excluded.country_name,
                        name_fa          = excluded.name_fa,
                        is_target_market = excluded.is_target_market,
                        priority         = excluded.priority,
                        updated_at       = excluded.updated_at;
                    """;

                cmd.Parameters.AddWithValue("$code", country.Code);
                cmd.Parameters.AddWithValue("$name", country.NameEn);
                cmd.Parameters.AddWithValue("$nameFa", country.NameFa);
                cmd.Parameters.AddWithValue("$target", country.IsTargetMarket ? 1 : 0);
                cmd.Parameters.AddWithValue("$priority", country.Priority);
                cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));

                affected += cmd.ExecuteNonQuery();
            }

            tx.Commit();
            return affected;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Countries", "countries could not be synced to the database: " + ex.Message);
            return 0;
        }
    }

    /// <summary>Keeps the display name usable when the file only supplies a code.</summary>
    private static CountryEntry Normalize(CountryEntry entry, CountryEntry? known) =>
        new()
        {
            Code = entry.Code,
            NameFa = string.IsNullOrWhiteSpace(entry.NameFa) ? known?.NameFa ?? "" : entry.NameFa,
            NameEn = string.IsNullOrWhiteSpace(entry.NameEn) ? known?.NameEn ?? entry.Code : entry.NameEn,
            IsTargetMarket = entry.IsTargetMarket,
            Priority = entry.Priority == 999 && known != null ? known.Priority : entry.Priority
        };

    private static IReadOnlyList<CountryEntry> Sort(IEnumerable<CountryEntry> entries) =>
        entries
            .OrderByDescending(e => e.IsTargetMarket)
            .ThenBy(e => e.Priority)
            .ThenBy(e => e.NameEn, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
