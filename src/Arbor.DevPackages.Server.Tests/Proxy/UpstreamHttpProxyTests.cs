using System.Net;
using System.Text;
using Arbor.DevPackages.Core.Feeds;
using Arbor.DevPackages.Core.Packages;
using Arbor.DevPackages.Server.Proxy;
using AwesomeAssertions;
using Xunit;

namespace Arbor.DevPackages.Server.Tests.Proxy;

/// <summary>
/// Unit tests for <see cref="UpstreamHttpProxy"/>.
/// A fake <see cref="HttpMessageHandler"/> and a real <see cref="FileSystemPackageStore"/>
/// (in a temp directory) are used so URL construction, 404 handling, and store interaction
/// are all exercised against the production code.
/// </summary>
public sealed class UpstreamHttpProxyTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemPackageStore _store;

    private static readonly FeedConfiguration Feed =
        new("test", new Uri("https://upstream.example.com/v3/flatcontainer/"));

    public UpstreamHttpProxyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        _store = new FileSystemPackageStore(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    // ─── FetchAndStoreAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task FetchAndStoreAsync_WhenNupkgNotFoundUpstream_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Setup(
            "https://upstream.example.com/v3/flatcontainer/testpkg/1.0.0/testpkg.1.0.0.nupkg",
            HttpStatusCode.NotFound);

        var proxy = BuildProxy(handler);
        var identity = new PackageIdentity("testpkg", "1.0.0");

        PackageMetadata? result =
            await proxy.FetchAndStoreAsync(identity, Feed, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task FetchAndStoreAsync_WhenPackageExistsUpstream_StoresAndReturnsMetadata()
    {
        byte[] nupkgBytes = Encoding.UTF8.GetBytes("fake-upstream-nupkg");
        const string nuspecXml =
            "<package><metadata><id>testpkg</id><version>1.0.0</version></metadata></package>";

        var handler = new FakeHttpMessageHandler();
        handler.Setup(
            "https://upstream.example.com/v3/flatcontainer/testpkg/1.0.0/testpkg.1.0.0.nupkg",
            HttpStatusCode.OK,
            nupkgBytes);
        handler.Setup(
            "https://upstream.example.com/v3/flatcontainer/testpkg/1.0.0/testpkg.1.0.0.nuspec",
            HttpStatusCode.OK,
            Encoding.UTF8.GetBytes(nuspecXml));

        var proxy = BuildProxy(handler);
        var identity = new PackageIdentity("testpkg", "1.0.0");

        PackageMetadata? result =
            await proxy.FetchAndStoreAsync(identity, Feed, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Identity.Id.Should().Be("testpkg");
        result.Identity.Version.Should().Be("1.0.0");
        result.NuspecContent.Should().Be(nuspecXml);
        result.Sha512Hash.Should().NotBeNullOrEmpty();

        // Verify the package was actually persisted in the store.
        bool exists = await _store.ExistsAsync(identity, CancellationToken.None);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task FetchAndStoreAsync_WhenNuspecMissingUpstream_ThrowsHttpRequestException()
    {
        byte[] nupkgBytes = Encoding.UTF8.GetBytes("fake-upstream-nupkg");

        var handler = new FakeHttpMessageHandler();
        handler.Setup(
            "https://upstream.example.com/v3/flatcontainer/testpkg/1.0.0/testpkg.1.0.0.nupkg",
            HttpStatusCode.OK,
            nupkgBytes);
        handler.Setup(
            "https://upstream.example.com/v3/flatcontainer/testpkg/1.0.0/testpkg.1.0.0.nuspec",
            HttpStatusCode.NotFound);

        var proxy = BuildProxy(handler);
        var identity = new PackageIdentity("testpkg", "1.0.0");

        Func<Task> act = () => proxy.FetchAndStoreAsync(identity, Feed, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task FetchAndStoreAsync_WhenUpstreamReturnsServerError_ThrowsHttpRequestException()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Setup(
            "https://upstream.example.com/v3/flatcontainer/testpkg/1.0.0/testpkg.1.0.0.nupkg",
            HttpStatusCode.InternalServerError);

        var proxy = BuildProxy(handler);
        var identity = new PackageIdentity("testpkg", "1.0.0");

        Func<Task> act = () => proxy.FetchAndStoreAsync(identity, Feed, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task FetchAndStoreAsync_ConstructsCorrectFlatContainerUrls()
    {
        var handler = new FakeHttpMessageHandler();
        // Return 404 for nupkg to short-circuit after URL construction is verified.
        handler.Setup(
            "https://upstream.example.com/v3/flatcontainer/mypkg/2.3.4/mypkg.2.3.4.nupkg",
            HttpStatusCode.NotFound);

        var proxy = BuildProxy(handler);
        var identity = new PackageIdentity("MyPkg", "2.3.4"); // mixed-case input

        await proxy.FetchAndStoreAsync(identity, Feed, CancellationToken.None);

        // The proxy must normalise the ID and version to lower-case in the URL.
        handler.RequestedUrls.Should().Contain(
            "https://upstream.example.com/v3/flatcontainer/mypkg/2.3.4/mypkg.2.3.4.nupkg");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private UpstreamHttpProxy BuildProxy(FakeHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var factory = new SingletonHttpClientFactory(httpClient);
        return new UpstreamHttpProxy(factory, _store);
    }

    /// <summary>A fake <see cref="HttpMessageHandler"/> with per-URL response setup.</summary>
    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, byte[]? Body)> _responses = new();

        public List<string> RequestedUrls { get; } = [];

        public void Setup(string url, HttpStatusCode status, byte[]? body = null)
            => _responses[url] = (status, body);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            RequestedUrls.Add(url);

            if (_responses.TryGetValue(url, out var entry))
            {
                var msg = new HttpResponseMessage(entry.Status);
                if (entry.Body is not null)
                {
                    msg.Content = new ByteArrayContent(entry.Body);
                }

                return Task.FromResult(msg);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    /// <summary>An <see cref="IHttpClientFactory"/> that always returns the same client.</summary>
    private sealed class SingletonHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public SingletonHttpClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }
}
