using Arbor.DevPackages.Core.Feeds;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NuGet.Versioning;

namespace Arbor.DevPackages.Server.Search;

public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearch(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v3/search", HandleSearchAsync);
        return app;
    }

    private static async Task<IResult> HandleSearchAsync(
        string feedId,
        IFeedRouter feedRouter,
        IUpstreamSearchCache cache,
        string? q = null,
        int skip = 0,
        int take = 20,
        bool prerelease = false,
        CancellationToken cancellationToken = default)
    {
        var feed = await feedRouter.RouteAsync(feedId, cancellationToken);
        if (feed is null)
        {
            return Results.NotFound();
        }

        // Feed-level AllowPrerelease acts as a cap: if the feed disallows prerelease,
        // prerelease packages are never returned regardless of the client's request.
        bool effectivePrerelease = prerelease && feed.AllowPrerelease;

        var allEntries = cache.GetAllEntries() ?? [];

        IEnumerable<SearchResultPackage> filtered = allEntries;

        if (!string.IsNullOrWhiteSpace(q))
        {
            filtered = filtered.Where(entry => MatchesQuery(entry, q));
        }

        if (!effectivePrerelease)
        {
            filtered = filtered
                .Where(HasStableVersion)
                .Select(ProjectToStableOnly);
        }

        var filteredList = filtered.ToList();
        var paged = filteredList.Skip(skip).Take(take).ToList();

        return Results.Json(
            new SearchQueryResponse(filteredList.Count, paged),
            contentType: "application/json");
    }

    private static bool MatchesQuery(SearchResultPackage entry, string q) =>
        ContainsCaseInsensitive(entry.Id, q) ||
        ContainsCaseInsensitive(entry.Description, q) ||
        ContainsCaseInsensitive(entry.Title, q) ||
        ContainsCaseInsensitive(entry.Tags, q);

    private static bool ContainsCaseInsensitive(string? value, string query) =>
        value is { } &&
        value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static bool HasStableVersion(SearchResultPackage entry)
    {
        if (entry.Versions is { Count: > 0 })
        {
            return entry.Versions.Any(versionEntry => !IsPrerelease(versionEntry.Version));
        }

        return !IsPrerelease(entry.Version);
    }

    /// <summary>
    /// Projects an entry to contain only stable versions and sets the top-level
    /// <see cref="SearchResultPackage.Version"/> to the latest stable version.
    /// Call only after <see cref="HasStableVersion"/> returns true.
    /// </summary>
    private static SearchResultPackage ProjectToStableOnly(SearchResultPackage entry)
    {
        if (entry.Versions is not { Count: > 0 })
        {
            // Top-level version only; HasStableVersion already confirmed it is stable.
            return entry;
        }

        var stableVersions = entry.Versions
            .Where(versionEntry => !IsPrerelease(versionEntry.Version))
            .ToList();

        var latestStable = stableVersions
            .Select(versionEntry => (versionEntry.Version, Parsed: NuGetVersion.TryParse(versionEntry.Version, out var parsedVersion) ? parsedVersion : null))
            .Where(parsedEntry => parsedEntry.Parsed is { })
            .OrderByDescending(parsedEntry => parsedEntry.Parsed)
            .Select(parsedEntry => parsedEntry.Version)
            .FirstOrDefault() ?? stableVersions[0].Version;

        return entry with { Version = latestStable, Versions = stableVersions };
    }

    private static bool IsPrerelease(string version) =>
        NuGetVersion.TryParse(version, out var nugetVersion) && nugetVersion.IsPrerelease;
}
