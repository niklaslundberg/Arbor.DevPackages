using Arbor.DevPackages.Core.Feeds;
using Arbor.DevPackages.Core.Packages;
using Arbor.DevPackages.Core.Proxy;

namespace Arbor.DevPackages.Server.Proxy;

/// <summary>
/// Fetches a package from the configured upstream NuGet v3 flat-container feed
/// and stores it in the local <see cref="IPackageStore"/>.
/// Uses <see cref="IHttpClientFactory"/> for outbound HTTP requests.
/// </summary>
public sealed class UpstreamHttpProxy : IUpstreamProxy
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPackageStore _packageStore;

    public UpstreamHttpProxy(IHttpClientFactory httpClientFactory, IPackageStore packageStore)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(packageStore);
        _httpClientFactory = httpClientFactory;
        _packageStore = packageStore;
    }

    /// <inheritdoc />
    public async Task<PackageMetadata?> FetchAndStoreAsync(
        PackageIdentity identity,
        FeedConfiguration feed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(feed);

        string id = identity.Id.ToLowerInvariant();
        string version = identity.Version.ToLowerInvariant();

        string baseUrl = feed.UpstreamUrl.ToString().TrimEnd('/');
        string nupkgUrl = $"{baseUrl}/{id}/{version}/{id}.{version}.nupkg";
        string nuspecUrl = $"{baseUrl}/{id}/{version}/{id}.{version}.nuspec";

        using HttpClient client = _httpClientFactory.CreateClient();

        using HttpResponseMessage nupkgResponse =
            await client.GetAsync(nupkgUrl, cancellationToken);

        if (nupkgResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        nupkgResponse.EnsureSuccessStatusCode();

        using HttpResponseMessage nuspecResponse =
            await client.GetAsync(nuspecUrl, cancellationToken);

        if (nuspecResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // .nupkg exists on upstream but .nuspec is missing — treat as upstream error.
            throw new HttpRequestException(
                $"Package {id} {version} .nupkg was found upstream but .nuspec returned 404.");
        }

        nuspecResponse.EnsureSuccessStatusCode();

        await using Stream nupkgStream =
            await nupkgResponse.Content.ReadAsStreamAsync(cancellationToken);
        await using Stream nuspecStream =
            await nuspecResponse.Content.ReadAsStreamAsync(cancellationToken);

        // StoreAsync handles non-seekable (HTTP response) streams via single-pass copy+hash.
        await _packageStore.StoreAsync(identity, nupkgStream, nuspecStream, cancellationToken);

        return await _packageStore.GetMetadataAsync(identity, cancellationToken);
    }
}
