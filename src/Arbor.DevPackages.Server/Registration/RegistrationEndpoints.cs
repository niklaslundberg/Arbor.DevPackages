using Arbor.DevPackages.Core.Packages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NuGet.Versioning;

namespace Arbor.DevPackages.Server.Registration;

public static class RegistrationEndpoints
{
    public static IEndpointRouteBuilder MapRegistration(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v3/registration");

        group.MapGet("{id}/index.json", GetRegistrationIndexAsync);
        group.MapGet("{id}/{version}.json", GetRegistrationLeafAsync);

        return app;
    }

    private static async Task<IResult> GetRegistrationIndexAsync(
        string id,
        IPackageStore store,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        id = id.ToLowerInvariant();

        var all = await store.ListAllAsync(cancellationToken);
        var identities = all
            .Where(p => string.Equals(p.Id, id, StringComparison.Ordinal))
            .OrderBy(p => p.Version, SemVerComparer.Instance)
            .ToArray();

        if (identities.Length == 0)
        {
            return Results.NotFound();
        }

        var metadataItems = new List<PackageMetadata>(identities.Length);
        foreach (var identity in identities)
        {
            var metadata = await store.GetMetadataAsync(identity, cancellationToken);
            if (metadata is not null)
            {
                metadataItems.Add(metadata);
            }
        }

        var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}";
        var response = RegistrationIndexBuilder.BuildIndex(baseUrl, id, metadataItems);

        return Results.Json(response, contentType: "application/json");
    }

    private static async Task<IResult> GetRegistrationLeafAsync(
        string id,
        string version,
        IPackageStore store,
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

        var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}";
        var response = RegistrationIndexBuilder.BuildLeaf(baseUrl, metadata);

        return Results.Json(response, contentType: "application/json");
    }

    private sealed class SemVerComparer : IComparer<string>
    {
        public static readonly SemVerComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            if (NuGetVersion.TryParse(x, out var vx) && NuGetVersion.TryParse(y, out var vy))
            {
                return VersionComparer.Default.Compare(vx, vy);
            }

            return StringComparer.Ordinal.Compare(x, y);
        }
    }
}
