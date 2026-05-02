using System.Net;
using System.Text;
using Arbor.DevPackages.Core.Feeds;
using Arbor.DevPackages.Core.Packages;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using PackageIdentity = Arbor.DevPackages.Core.Packages.PackageIdentity;

namespace Arbor.DevPackages.Server.Push;

public static class PushEndpoints
{
    public static IEndpointRouteBuilder MapPush(this IEndpointRouteBuilder app)
    {
        app.MapPut("/v3/push", PushPackageAsync);
        return app;
    }

    private static async Task<IResult> PushPackageAsync(
        string feedId,
        IFeedRouter feedRouter,
        IPackageStore store,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        // Restrict push to loopback connections only (no authentication is implemented).
        // Null remote IP means in-process / TestServer — allow those so tests work.
        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp is not null && !IPAddress.IsLoopback(remoteIp))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var feed = await feedRouter.RouteAsync(feedId, cancellationToken);
        if (feed is null)
        {
            return Results.NotFound();
        }

        if (!feed.AllowPush)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (!context.Request.HasFormContentType)
        {
            return Results.BadRequest("Expected multipart/form-data content.");
        }

        var form = await context.Request.ReadFormAsync(cancellationToken);
        if (form.Files.Count == 0)
        {
            return Results.BadRequest("No package file found in the request.");
        }

        // NuGet clients typically name the file field "package" or use the filename.
        var file = form.Files["package"] ?? form.Files[0];

        // Buffer the nupkg so we can read it twice: once for identity extraction and once for storage.
        using var nupkgBuffer = new MemoryStream();
        await using (var uploadStream = file.OpenReadStream())
        {
            await uploadStream.CopyToAsync(nupkgBuffer, cancellationToken);
        }

        nupkgBuffer.Position = 0;

        // Extract the package identity and nuspec content using the official NuGet.Packaging reader.
        string nuspecContent;
        PackageIdentity identity;

        try
        {
            using var archiveReader = new PackageArchiveReader(nupkgBuffer, leaveStreamOpen: true);
            var nuspecReader = archiveReader.NuspecReader;
            var nugetIdentity = nuspecReader.GetIdentity();

            identity = new PackageIdentity(
                nugetIdentity.Id.ToLowerInvariant(),
                nugetIdentity.Version.ToNormalizedString());

            await using var nuspecStream = archiveReader.GetNuspec();
            using var reader = new StreamReader(nuspecStream, Encoding.UTF8);
            nuspecContent = await reader.ReadToEndAsync(cancellationToken);
        }
        catch (InvalidDataException)
        {
            return Results.BadRequest("The uploaded file is not a valid NuGet package.");
        }
        catch (PackagingException ex)
        {
            return Results.BadRequest(ex.Message);
        }

        // Reject pre-release packages if the feed does not allow them.
        if (!NuGet.Versioning.NuGetVersion.TryParse(identity.Version, out var nugetVersion))
        {
            return Results.BadRequest($"The version '{identity.Version}' is not a valid NuGet version.");
        }

        if (nugetVersion.IsPrerelease && !feed.AllowPrerelease)
        {
            return Results.StatusCode(StatusCodes.Status422UnprocessableEntity);
        }

        nupkgBuffer.Position = 0;
        using var storedNuspecStream = new MemoryStream(Encoding.UTF8.GetBytes(nuspecContent));

        var result = await store.StoreAsync(identity, nupkgBuffer, storedNuspecStream, cancellationToken);

        return result switch
        {
            PackageStoreResult.Stored => Results.StatusCode(StatusCodes.Status201Created),
            PackageStoreResult.AlreadyExists => Results.Conflict(),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)   // coverage: unreachable
        };
    }
}
