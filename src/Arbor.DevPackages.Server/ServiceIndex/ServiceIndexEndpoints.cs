using System.Text.Json.Serialization;
using Arbor.DevPackages.Core.Feeds;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Arbor.DevPackages.Server.ServiceIndex;

public static class ServiceIndexEndpoints
{
    public static IEndpointRouteBuilder MapServiceIndex(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v3/index.json", GetServiceIndexAsync);
        return app;
    }

    private static async Task<IResult> GetServiceIndexAsync(
        string feedId,
        IFeedRouter feedRouter,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var feed = await feedRouter.RouteAsync(feedId, cancellationToken);
        if (feed is null)
        {
            return Results.NotFound();
        }

        var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}/feeds/{feedId}";

        var index = new ServiceIndexResponse(
            Version: "3.0.0",
            Resources:
            [
                new ServiceIndexEntry(
                    Id: $"{baseUrl}/v3/flatcontainer/",
                    Type: "PackageBaseAddress/3.0.0",
                    Comment: "Base URL of where NuGet packages are stored"),
                new ServiceIndexEntry(
                    Id: $"{baseUrl}/v3/registration/",
                    Type: "RegistrationsBaseUrl/3.6.0",
                    Comment: "Base URL of NuGet package registration info"),
                new ServiceIndexEntry(
                    Id: $"{baseUrl}/v3/search",
                    Type: "SearchQueryService/3.5.0",
                    Comment: "Query endpoint of NuGet Search service"),
                new ServiceIndexEntry(
                    Id: $"{baseUrl}/v3/search",
                    Type: "SearchQueryService/3.0.0-beta",
                    Comment: "Query endpoint of NuGet Search service (legacy type alias)")
            ]);

        return Results.Json(index, contentType: "application/json");
    }
}

internal sealed record ServiceIndexResponse(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("resources")] IReadOnlyList<ServiceIndexEntry> Resources);

internal sealed record ServiceIndexEntry(
    [property: JsonPropertyName("@id")] string Id,
    [property: JsonPropertyName("@type")] string Type,
    [property: JsonPropertyName("comment")] string Comment);
