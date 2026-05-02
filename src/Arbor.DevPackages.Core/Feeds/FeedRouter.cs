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
        ValidateFeedIds(feeds);
        _feeds = feeds.ToDictionary(f => f.FeedId, StringComparer.OrdinalIgnoreCase);
    }

    public Task<FeedConfiguration?> RouteAsync(string feedId, CancellationToken cancellationToken)
    {
        _feeds.TryGetValue(feedId, out var feed);
        return Task.FromResult(feed);
    }

    /// <summary>
    /// Validates that all feed IDs are non-empty, URL-safe path segments, and unique
    /// (case-insensitive). Throws <see cref="InvalidOperationException"/> with a clear message
    /// on the first violation so the server fails fast with actionable feedback.
    /// </summary>
    private static void ValidateFeedIds(IReadOnlyList<FeedConfiguration> feeds)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var feed in feeds)
        {
            var id = feed.FeedId;

            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidOperationException(
                    "Feed ID must not be empty or whitespace.");
            }

            // Reject characters that are invalid or special in a URL path segment.
            // '/' splits the path; '?' starts the query string; '#' starts a fragment;
            // '%' is the percent-encoding prefix.
            foreach (var ch in id)
            {
                if (ch is '/' or '?' or '#' or '%' || char.IsControl(ch) || char.IsWhiteSpace(ch))
                {
                    throw new InvalidOperationException(
                        $"Feed ID '{id}' contains an invalid character '{ch}'. " +
                        "Feed IDs must be valid URL path segment characters.");
                }
            }

            if (!seen.Add(id))
            {
                throw new InvalidOperationException(
                    $"Duplicate feed ID '{id}' detected (comparison is case-insensitive). " +
                    "Each feed must have a unique ID.");
            }
        }
    }
}
