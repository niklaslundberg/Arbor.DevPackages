using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NuGet.Versioning;

namespace Arbor.DevPackages.Server.Search;

public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearch(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v3/search", HandleSearch);
        return app;
    }

    private static IResult HandleSearch(
        IUpstreamSearchCache cache,
        string? q = null,
        int skip = 0,
        int take = 20,
        bool prerelease = false)
    {
        var allEntries = cache.GetAllEntries() ?? [];

        IEnumerable<SearchResultPackage> filtered = allEntries;

        if (!string.IsNullOrWhiteSpace(q))
        {
            filtered = filtered.Where(e => MatchesQuery(e, q));
        }

        if (!prerelease)
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
        value is not null &&
        value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static bool HasStableVersion(SearchResultPackage entry)
    {
        if (entry.Versions is { Count: > 0 })
        {
            return entry.Versions.Any(v => !IsPrerelease(v.Version));
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
            .Where(v => !IsPrerelease(v.Version))
            .ToList();

        var latestStable = stableVersions
            .Select(v => (v.Version, Parsed: NuGetVersion.TryParse(v.Version, out var nv) ? nv : null))
            .Where(x => x.Parsed is not null)
            .OrderByDescending(x => x.Parsed)
            .Select(x => x.Version)
            .FirstOrDefault() ?? stableVersions[0].Version;

        return entry with { Version = latestStable, Versions = stableVersions };
    }

    private static bool IsPrerelease(string version) =>
        NuGetVersion.TryParse(version, out var v) && v.IsPrerelease;
}
