namespace Arbor.DevPackages.Server.Search;

/// <summary>
/// Provides access to cached upstream search results and on-demand refresh.
/// </summary>
public interface IUpstreamSearchCache
{
    /// <summary>
    /// Returns all cached search result entries, or <c>null</c> if no results have been fetched yet.
    /// </summary>
    IReadOnlyList<SearchResultPackage>? GetAllEntries();

    /// <summary>
    /// Triggers an immediate refresh of the upstream search cache.
    /// On failure, the existing cached entries are retained (stale-if-error).
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken);
}
