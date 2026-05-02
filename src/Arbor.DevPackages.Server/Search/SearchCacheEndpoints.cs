using Arbor.DevPackages.Core.Feeds;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Arbor.DevPackages.Server.Search;

public static class SearchCacheEndpoints
{
    public static IEndpointRouteBuilder MapSearchCache(this IEndpointRouteBuilder app)
    {
        app.MapPost(
            "/api/feeds/{feedId}/search-cache/refresh",
            RefreshCacheAsync);
        return app;
    }

    private static async Task<IResult> RefreshCacheAsync(
        string feedId,
        FeedConfiguration feed,
        IUpstreamSearchCache cache,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(feedId, feed.FeedId, StringComparison.Ordinal))
        {
            return Results.NotFound();
        }

        await cache.RefreshAsync(cancellationToken);
        return Results.Ok();
    }
}
