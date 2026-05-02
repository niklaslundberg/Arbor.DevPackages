using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Arbor.DevPackages.Core.Feeds;
using Arbor.DevPackages.Core.Packages;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NuGet.Versioning;

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

        // Buffer the nupkg so we can read it twice: once for nuspec extraction and once for storage.
        using var nupkgBuffer = new MemoryStream();
        await using (var uploadStream = file.OpenReadStream())
        {
            await uploadStream.CopyToAsync(nupkgBuffer, cancellationToken);
        }

        nupkgBuffer.Position = 0;

        // Extract the nuspec from the nupkg (which is a ZIP file).
        string nuspecContent;
        PackageIdentity identity;

        try
        {
            using var archive = new ZipArchive(nupkgBuffer, ZipArchiveMode.Read, leaveOpen: true);
            var nuspecEntry = archive.Entries
                .FirstOrDefault(e => e.Name.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));

            if (nuspecEntry is null)
            {
                return Results.BadRequest("No .nuspec file found in the package.");
            }

            await using var nuspecEntryStream = nuspecEntry.Open();
            using var reader = new StreamReader(nuspecEntryStream, Encoding.UTF8);
            nuspecContent = await reader.ReadToEndAsync(cancellationToken);

            identity = ExtractPackageIdentity(nuspecContent);
        }
        catch (InvalidDataException)
        {
            return Results.BadRequest("The uploaded file is not a valid NuGet package.");
        }
        catch (FormatException ex)
        {
            return Results.BadRequest(ex.Message);
        }

        // Reject pre-release packages if the feed does not allow them.
        if (NuGetVersion.TryParse(identity.Version, out var nugetVersion) &&
            nugetVersion.IsPrerelease &&
            !feed.AllowPrerelease)
        {
            return Results.StatusCode(StatusCodes.Status422UnprocessableEntity);
        }

        nupkgBuffer.Position = 0;
        using var nuspecStream = new MemoryStream(Encoding.UTF8.GetBytes(nuspecContent));

        var result = await store.StoreAsync(identity, nupkgBuffer, nuspecStream, cancellationToken);

        return result switch
        {
            PackageStoreResult.Stored => Results.StatusCode(StatusCodes.Status201Created),
            PackageStoreResult.AlreadyExists => Results.Conflict(),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)   // coverage: unreachable
        };
    }

    public static PackageIdentity ExtractPackageIdentity(string nuspecContent)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(nuspecContent);
        }
        catch (Exception ex)
        {
            throw new FormatException("The .nuspec file is not valid XML.", ex);
        }

        // Use LocalName to support any NuGet nuspec namespace variant.
        string id = doc.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "id")?.Value ?? "";
        string version = doc.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "version")?.Value ?? "";

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version))
        {
            throw new FormatException("The .nuspec file is missing the required 'id' or 'version' element.");
        }

        return new PackageIdentity(id.ToLowerInvariant(), version.ToLowerInvariant());
    }
}
