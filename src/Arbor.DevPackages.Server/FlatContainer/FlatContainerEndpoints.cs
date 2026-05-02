using System.Text;
using System.Text.Json.Serialization;
using Arbor.DevPackages.Core.Feeds;
using Arbor.DevPackages.Core.Packages;
using Arbor.DevPackages.Core.Proxy;
using Arbor.DevPackages.Core.Statistics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

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
            .OrderBy(v => v, SemVerComparer.Instance)
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

        // Step 1: Check local store.
        var storedHash = await store.GetStoredHashAsync(identity, cancellationToken);

        if (storedHash is null)
        {
            // Resolve optional proxy services from DI (null if not configured).
            var probe = context.RequestServices.GetService<IConnectivityProbe>();
            var upstreamProxy = context.RequestServices.GetService<IUpstreamProxy>();
            var feed = context.RequestServices.GetService<FeedConfiguration>();

            // Step 2: If proxy services are not configured, fall back to 404.
            if (probe is null || upstreamProxy is null || feed is null)
            {
                return Results.NotFound();
            }

            bool reachable = await probe.IsReachableAsync(feed, cancellationToken);
            if (!reachable)
            {
                return Results.NotFound();
            }

            // Step 3: Fetch from upstream and store locally.
            PackageMetadata? fetched;
            try
            {
                fetched = await upstreamProxy.FetchAndStoreAsync(identity, feed, cancellationToken);
            }
            catch (HttpRequestException)
            {
                // Step 5: Upstream HTTP error — mark upstream offline and return 502.
                // Only HttpRequestException is caught here; local storage errors (IOException,
                // PackageIntegrityException) propagate and result in a 500 response so that
                // disk problems are not misattributed to the upstream.
                await probe.RecordFailureAsync(feed, cancellationToken);
                return Results.StatusCode(StatusCodes.Status502BadGateway);
            }

            if (fetched is null)
            {
                // Package does not exist on the upstream feed.
                return Results.NotFound();
            }

            // Step 4: Package was stored — re-read the stored hash from the local store.
            storedHash = await store.GetStoredHashAsync(identity, cancellationToken);
            if (storedHash is null)
            {
                return Results.Problem(
                    detail: $"Package {identity.Id} {identity.Version} was fetched from upstream but could not be read back from the store.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        // GetStoredHashAsync is a cheap read (no hash re-computation) used only for ETag.
        var quotedEtag = $"\"{storedHash}\"";

        var ifNoneMatch = context.Request.GetTypedHeaders().IfNoneMatch;
        if (ifNoneMatch is { Count: > 0 } &&
            ifNoneMatch.Any(e => e.Tag.HasValue &&
                string.Equals(e.Tag.Value, quotedEtag, StringComparison.Ordinal)))
        {
            return Results.StatusCode(304);
        }

        // OpenNupkgAsync performs integrity verification (single hash computation).
        var stream = await store.OpenNupkgAsync(identity, cancellationToken);
        if (stream is null)
        {
            // Stored hash existed but .nupkg stream is missing — integrity violation.
            return Results.Problem(
                detail: $"Package {id} {version} hash record found but .nupkg is missing.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        await statistics.RecordDownloadAsync(
            new DownloadEvent(identity, DateTimeOffset.UtcNow),
            cancellationToken);

        context.Response.Headers.ETag = quotedEtag;
        context.Response.Headers["X-Checksum-SHA512"] = storedHash;

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

        // GetMetadataAsync verifies .nupkg integrity before returning nuspec content,
        // ensuring we never serve a nuspec for a package that fails its integrity check.
        var metadata = await store.GetMetadataAsync(identity, cancellationToken);
        if (metadata is null)
        {
            return Results.NotFound();
        }

        var bytes = Encoding.UTF8.GetBytes(metadata.NuspecContent);
        return Results.Bytes(bytes, contentType: "application/xml");
    }

}

internal sealed record VersionListResponse(
    [property: JsonPropertyName("versions")] string[] Versions);


