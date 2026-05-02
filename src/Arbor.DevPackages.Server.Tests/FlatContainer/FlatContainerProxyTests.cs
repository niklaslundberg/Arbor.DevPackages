using System.Net;
using System.Text;
using Arbor.DevPackages.Core.Feeds;
using Arbor.DevPackages.Core.Packages;
using Arbor.DevPackages.Core.Proxy;
using Arbor.DevPackages.Core.Statistics;
using Arbor.DevPackages.Testing;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Arbor.DevPackages.Server.Tests.FlatContainer;

/// <summary>
/// Integration tests for the read-through proxy workflow in
/// <c>FlatContainerEndpoints.DownloadNupkgAsync</c>.
/// </summary>
public sealed class FlatContainerProxyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly PackageIdentity TestIdentity = new("testpkg", "1.0.0");

    private static readonly byte[] TestNupkg =
        Encoding.UTF8.GetBytes("fake-proxy-nupkg-content");

    private static readonly string TestNuspec =
        "<package><metadata><id>testpkg</id><version>1.0.0</version></metadata></package>";

    public FlatContainerProxyTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private WebApplicationFactory<Program> BuildFactory(
        InMemoryPackageStore? store = null,
        FakeConnectivityProbe? probe = null,
        FakeUpstreamProxy? upstreamProxy = null)
    {
        var packageStore = store ?? new InMemoryPackageStore();
        return _factory.WithWebHostBuilder(b =>
            b.ConfigureServices(services =>
            {
                services.AddSingleton<IPackageStore>(packageStore);
                services.AddSingleton<IStatisticsCollector>(new RecordingStatisticsCollector());

                if (probe is not null)
                {
                    services.AddSingleton<IConnectivityProbe>(probe);
                }

                if (upstreamProxy is not null)
                {
                    services.AddSingleton<IUpstreamProxy>(upstreamProxy);
                }
            }));
    }

    // ─── DownloadNupkg_NotCachedUpstreamOnline_FetchesStoresAndServes ────────

    [Fact]
    public async Task DownloadNupkg_NotCachedUpstreamOnline_FetchesStoresAndServes()
    {
        // Arrange: empty local store; probe reports upstream reachable.
        var store = new InMemoryPackageStore();
        var probe = new FakeConnectivityProbe(isReachable: true);

        // When the proxy is called it adds the package to the same store instance,
        // so the handler can serve it on re-read.
        var upstreamProxy = new FakeUpstreamProxy(async (identity, feed, ct) =>
        {
            store.Add(identity, TestNupkg, TestNuspec);
            return await store.GetMetadataAsync(identity, ct);
        });

        using var factory = BuildFactory(store: store, probe: probe, upstreamProxy: upstreamProxy);
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync(
            "/v3/flatcontainer/testpkg/1.0.0/testpkg.1.0.0.nupkg");

        // Assert: package should be fetched from upstream and served.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/octet-stream");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().BeEquivalentTo(TestNupkg);
    }

    // ─── DownloadNupkg_NotCachedUpstreamOffline_Returns404 ───────────────────

    [Fact]
    public async Task DownloadNupkg_NotCachedUpstreamOffline_Returns404()
    {
        // Arrange: empty local store; probe reports upstream is offline.
        var store = new InMemoryPackageStore();
        var probe = new FakeConnectivityProbe(isReachable: false);

        using var factory = BuildFactory(store: store, probe: probe);
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync(
            "/v3/flatcontainer/testpkg/1.0.0/testpkg.1.0.0.nupkg");

        // Assert: 404 while the upstream is within its back-off window.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── DownloadNupkg_UpstreamFetchFails_Returns502AndMarksOffline ──────────

    [Fact]
    public async Task DownloadNupkg_UpstreamFetchFails_Returns502AndMarksOffline()
    {
        // Arrange: empty local store; probe reports upstream reachable;
        // proxy throws to simulate an upstream error.
        var store = new InMemoryPackageStore();
        var probe = new FakeConnectivityProbe(isReachable: true);
        var upstreamProxy = new FakeUpstreamProxy((_, _, _) =>
            Task.FromException<PackageMetadata?>(
                new HttpRequestException("Upstream is unavailable")));

        using var factory = BuildFactory(store: store, probe: probe, upstreamProxy: upstreamProxy);
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync(
            "/v3/flatcontainer/testpkg/1.0.0/testpkg.1.0.0.nupkg");

        // Assert: 502 is returned and the upstream is marked offline.
        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        probe.FailureCount.Should().Be(1);
    }
}
