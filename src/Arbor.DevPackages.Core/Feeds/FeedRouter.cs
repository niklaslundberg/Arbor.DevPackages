namespace Arbor.DevPackages.Core.Feeds;

/// <summary>
/// Routes a feed ID to the corresponding <see cref="FeedConfiguration"/>.
/// Lookup is case-insensitive and O(1).
/// </summary>
public sealed class FeedRouter : IFeedRouter
{
    private readonly IReadOnlyDictionary<string, FeedConfiguration> _feeds;

    public FeedRouter(IReadOnlyList<FeedConfiguration> feeds)
    {
        ArgumentNullException.ThrowIfNull(feeds);
        _feeds = feeds.ToDictionary(f => f.FeedId, StringComparer.OrdinalIgnoreCase);
    }

    public Task<FeedConfiguration?> RouteAsync(string feedId, CancellationToken cancellationToken)
    {
        _feeds.TryGetValue(feedId, out var feed);
        return Task.FromResult(feed);
    }
}
