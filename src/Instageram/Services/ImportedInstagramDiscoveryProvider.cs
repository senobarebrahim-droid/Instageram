using System.IO;
using System.Text.RegularExpressions;

namespace Instageram;

public sealed class ImportedInstagramDiscoveryProvider
    : IInstagramDiscoveryProvider
{
    private readonly string _filePath;

    public ImportedInstagramDiscoveryProvider()
    {
        _filePath = Path.Combine(
            AppContext.BaseDirectory,
            "config",
            "instagram_users.txt");

        if (!File.Exists(_filePath))
        {
            var dir = Directory.GetParent(
                AppContext.BaseDirectory);

            if (dir != null)
            {
                var source = Path.Combine(
                    dir.Parent?.Parent?.Parent?.FullName ?? "",
                    "config",
                    "instagram_users.txt");

                if (File.Exists(source))
                    _filePath = source;
            }
        }
    }

    public string Name => "Local Import";

    /// <summary>
    /// Phase 3: the file this provider actually reads, so the UI can tell the
    /// user exactly which path to edit.
    /// </summary>
    public string FilePath => _filePath;

    public async Task<IReadOnlyList<InstagramDiscoveryCandidate>> DiscoverAsync(
        int requestedCount,
        IEnumerable<string>? countries = null,
        CancellationToken cancellationToken = default)
    {
        if (requestedCount <= 0)
            return Array.Empty<InstagramDiscoveryCandidate>();

        if (!File.Exists(_filePath))
            return Array.Empty<InstagramDiscoveryCandidate>();

        var lines = await File.ReadAllLinesAsync(
            _filePath,
            cancellationToken);

        var result = new List<InstagramDiscoveryCandidate>();

        var seen = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach(var raw in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var value = raw.Trim();

            if(string.IsNullOrWhiteSpace(value))
                continue;

            if(value.StartsWith("#"))
                continue;

            if(value.StartsWith("@"))
                value=value[1..];

            if(Uri.TryCreate(
                value,
                UriKind.Absolute,
                out var uri))
            {
                if(uri.Host.Contains(
                    "instagram.com",
                    StringComparison.OrdinalIgnoreCase))
                {
                    value=uri.AbsolutePath
                        .Trim('/')
                        .Split('/')[0];
                }
            }

            if(!Regex.IsMatch(
                value,
                "^[A-Za-z0-9._]{1,30}$"))
                continue;

            if(!seen.Add(value))
                continue;

            result.Add(new InstagramDiscoveryCandidate
            {
                Username=value,
                ProfileUrl=$"https://www.instagram.com/{value}/",
                Country=string.Empty
            });

            if(result.Count>=requestedCount)
                break;
        }

        return result;
    }
}

