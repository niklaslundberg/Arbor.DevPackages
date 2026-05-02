using System.Text.Json.Serialization;
using Arbor.DevPackages.Core.Statistics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Arbor.DevPackages.Server.Stats;

public static class StatsEndpoints
{
    public static IEndpointRouteBuilder MapStats(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/stats", GetStatsAsync);
        return app;
    }

    private static async Task<IResult> GetStatsAsync(
        IStatisticsReader statisticsReader,
        CancellationToken cancellationToken)
    {
        var allStats = await statisticsReader.GetAllPackageStatsAsync(cancellationToken);

        var packages = allStats
            .Select(stat => new StatsPackageEntry(
                Id: stat.Identity.Id,
                Version: stat.Identity.Version,
                DownloadCount: stat.DownloadCount,
                LastDownloadedAt: stat.LastDownloadedAt))
            .ToArray();

        return Results.Json(new StatsResponse(Packages: packages));
    }
}

internal sealed record StatsResponse(
    [property: JsonPropertyName("packages")] IReadOnlyList<StatsPackageEntry> Packages);

internal sealed record StatsPackageEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("downloadCount")] long DownloadCount,
    [property: JsonPropertyName("lastDownloadedAt")] DateTimeOffset? LastDownloadedAt);
