using System.Text.Json.Serialization;
using Arbor.DevPackages.Core.Packages;
using Arbor.DevPackages.Core.Statistics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Arbor.DevPackages.Server.FlatContainer;

public static class FlatContainerEndpoints
{
    public static IEndpointRouteBuilder MapFlatContainer(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v3/flatcontainer");

        group.MapGet("{id}/index.json", GetVersionListAsync);
        group.MapGet("{id}/{version}/{fileName}.nupkg", DownloadNupkgAsync);
        group.MapGet("{id}/{version}/{fileName}.nuspec", DownloadNuspecAsync);

        return app;
    }

    private static async Task<IResult> GetVersionListAsync(
        string id,
        IPackageStore store,
        CancellationToken cancellationToken)
    {
        id = id.ToLowerInvariant();

        var all = await store.ListAllAsync(cancellationToken);
        var versions = all
            .Where(p => string.Equals(p.Id, id, StringComparison.Ordinal))
            .Select(p => p.Version)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToArray();

        if (versions.Length == 0)
        {
            return Results.NotFound();
        }

        return Results.Ok(new VersionListResponse(versions));
    }

    private static async Task<IResult> DownloadNupkgAsync(
        string id,
        string version,
        string fileName,
        IPackageStore store,
        IStatisticsCollector statistics,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        id = id.ToLowerInvariant();
        version = version.ToLowerInvariant();

        var identity = new PackageIdentity(id, version);
        var metadata = await store.GetMetadataAsync(identity, cancellationToken);

        if (metadata is null)
        {
            return Results.NotFound();
        }

        var etag = metadata.Sha512Hash;
        var quotedEtag = $"\"{etag}\"";

        var ifNoneMatch = context.Request.GetTypedHeaders().IfNoneMatch;
        if (ifNoneMatch is { Count: > 0 } &&
            ifNoneMatch.Any(e => e.Tag.HasValue &&
                string.Equals(e.Tag.Value, quotedEtag, StringComparison.Ordinal)))
        {
            return Results.StatusCode(304);
        }

        var stream = await store.OpenNupkgAsync(identity, cancellationToken);
        if (stream is null)
        {
            // Metadata existed but .nupkg is missing — integrity violation.
            return Results.Problem(
                detail: $"Package {id} {version} metadata found but .nupkg is missing.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        await statistics.RecordDownloadAsync(
            new DownloadEvent(identity, DateTimeOffset.UtcNow),
            cancellationToken);

        context.Response.Headers.ETag = quotedEtag;
        context.Response.Headers["X-Checksum-SHA512"] = etag;

        return Results.Stream(stream, contentType: "application/octet-stream");
    }

    private static async Task<IResult> DownloadNuspecAsync(
        string id,
        string version,
        string fileName,
        IPackageStore store,
        CancellationToken cancellationToken)
    {
        id = id.ToLowerInvariant();
        version = version.ToLowerInvariant();

        var identity = new PackageIdentity(id, version);
        var stream = await store.OpenNuspecAsync(identity, cancellationToken);

        if (stream is null)
        {
            return Results.NotFound();
        }

        return Results.Stream(stream, contentType: "application/xml");
    }
}

internal sealed record VersionListResponse(
    [property: JsonPropertyName("versions")] string[] Versions);
