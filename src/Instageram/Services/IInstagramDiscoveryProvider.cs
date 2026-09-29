namespace Instageram;

public interface IInstagramDiscoveryProvider
{
    string Name { get; }

    Task<IReadOnlyList<InstagramDiscoveryCandidate>> DiscoverAsync(
        int requestedCount,
        IEnumerable<string>? countries = null,
        CancellationToken cancellationToken = default);
}
