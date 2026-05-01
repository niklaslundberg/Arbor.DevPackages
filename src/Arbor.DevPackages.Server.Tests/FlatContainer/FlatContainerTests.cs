using System.Net;
using System.Text;
using System.IO.Compression;
using System.Text.Json;
using Arbor.DevPackages.Core.Packages;
using Arbor.DevPackages.Core.Statistics;
using Arbor.DevPackages.Server.FlatContainer;
using Arbor.DevPackages.Server.ServiceIndex;
using Arbor.DevPackages.Testing;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Xunit;

namespace Arbor.DevPackages.Server.Tests.FlatContainer;

public sealed class FlatContainerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly PackageIdentity TestIdentity =
        new("Serilog", "3.1.1");

    private static readonly byte[] TestNupkg =
        Encoding.UTF8.GetBytes("fake-nupkg-content");

    private static readonly string TestNuspec =
        "<package><metadata><id>Serilog</id><version>3.1.1</version></metadata></package>";

    public FlatContainerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private WebApplicationFactory<Program> BuildFactory(
        InMemoryPackageStore? store = null,
        RecordingStatisticsCollector? collector = null)
    {
        var packageStore = store ?? new InMemoryPackageStore();
        var statsCollector = collector ?? new RecordingStatisticsCollector();

        return _factory.WithWebHostBuilder(b =>
            b.ConfigureServices(services =>
            {
                services.AddSingleton<IPackageStore>(packageStore);
                services.AddSingleton<IStatisticsCollector>(statsCollector);
            }));
    }

    private static InMemoryPackageStore StoreWithTestPackage()
    {
        var store = new InMemoryPackageStore();
        store.Add(TestIdentity, TestNupkg, TestNuspec);
        return store;
    }

    // ─── Version list ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetVersionList_KnownPackage_ReturnsVersionArray()
    {
        using var factory = BuildFactory(StoreWithTestPackage());
        var client = factory.CreateClient();

        var response = await client.GetAsync("/v3/flatcontainer/serilog/index.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var versions = doc.RootElement.GetProperty("versions");

        versions.GetArrayLength().Should().Be(1);
        versions[0].GetString().Should().Be("3.1.1");
    }

    [Fact]
    public async Task GetVersionList_UnknownPackage_Returns404()
    {
        using var factory = BuildFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/v3/flatcontainer/unknown-package/index.json");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── Nupkg download ──────────────────────────────────────────────────────

    [Fact]
    public async Task DownloadNupkg_KnownPackage_ReturnsOctetStreamWithHash()
    {
        using var factory = BuildFactory(StoreWithTestPackage());
        var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/v3/flatcontainer/serilog/3.1.1/serilog.3.1.1.nupkg");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/octet-stream");
        response.Headers.Contains("X-Checksum-SHA512").Should().BeTrue();

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().BeEquivalentTo(TestNupkg);
    }

    [Fact]
    public async Task DownloadNupkg_UnknownPackage_Returns404()
    {
        using var factory = BuildFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/v3/flatcontainer/unknown/1.0.0/unknown.1.0.0.nupkg");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DownloadNupkg_WithMatchingEtag_Returns304()
    {
        using var factory = BuildFactory(StoreWithTestPackage());
        var client = factory.CreateClient();

        // First request to get the ETag.
        var first = await client.GetAsync(
            "/v3/flatcontainer/serilog/3.1.1/serilog.3.1.1.nupkg");
        var etag = first.Headers.ETag?.Tag;
        etag.Should().NotBeNullOrEmpty();

        // Second request with matching If-None-Match.
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/v3/flatcontainer/serilog/3.1.1/serilog.3.1.1.nupkg");
        request.Headers.Add("If-None-Match", etag!);
        var second = await client.SendAsync(request);

        second.StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task DownloadNupkg_RecordsDownloadStatistic()
    {
        var collector = new RecordingStatisticsCollector();
        using var factory = BuildFactory(StoreWithTestPackage(), collector);
        var client = factory.CreateClient();

        await client.GetAsync("/v3/flatcontainer/serilog/3.1.1/serilog.3.1.1.nupkg");

        collector.RecordedEvents.Should().HaveCount(1);
        var evt = collector.RecordedEvents[0];
        evt.Identity.Id.Should().Be("serilog");
        evt.Identity.Version.Should().Be("3.1.1");
    }

    // ─── Nuspec download ─────────────────────────────────────────────────────

    [Fact]
    public async Task DownloadNuspec_KnownPackage_ReturnsXml()
    {
        using var factory = BuildFactory(StoreWithTestPackage());
        var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/v3/flatcontainer/serilog/3.1.1/serilog.3.1.1.nuspec");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/xml");

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be(TestNuspec);
    }

    // ─── NuGet.Protocol end-to-end ───────────────────────────────────────────

    /// <summary>
    /// Creates a minimal valid nupkg (ZIP) byte array containing the nuspec.
    /// NuGet.Protocol validates that a downloaded package is a valid ZIP archive;
    /// raw bytes that are not a ZIP would cause <c>CopyNupkgToStreamAsync</c> to return false.
    /// </summary>
    private static byte[] CreateMinimalNupkgBytes()
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("serilog.3.1.1.nuspec");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(TestNuspec);
        }

        return ms.ToArray();
    }

    [Fact]
    public async Task DownloadNupkg_NuGetProtocolClient_CanListVersionsAndDownloadPackage()
    {
        // NuGet.Protocol validates that a downloaded .nupkg is a valid ZIP file,
        // so we must provide one.
        var nupkgBytes = CreateMinimalNupkgBytes();

        var store = new InMemoryPackageStore();
        store.Add(TestIdentity, nupkgBytes, TestNuspec);
        var collector = new RecordingStatisticsCollector();

        // Start a real Kestrel server so NuGet.Protocol uses its own HTTP stack.
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        Arbor.DevPackages.ServiceDefaults.Extensions.AddServiceDefaults(builder);
        builder.Services.AddSingleton<IPackageStore>(store);
        builder.Services.AddSingleton<IStatisticsCollector>(collector);

        await using var app = builder.Build();
        Arbor.DevPackages.ServiceDefaults.Extensions.MapDefaultEndpoints(app);
        ServiceIndexEndpoints.MapServiceIndex(app);
        FlatContainerEndpoints.MapFlatContainer(app);

        await app.StartAsync();

        try
        {
            var indexUrl = app.Urls.FirstOrDefault() is { } url
                ? $"{url}/v3/index.json"
                : throw new InvalidOperationException("The test server did not bind to any address.");

            var source = new PackageSource(indexUrl);
            var repository = Repository.Factory.GetCoreV3(source);

            using var cache = new SourceCacheContext { NoCache = true };
            var resource = await repository.GetResourceAsync<FindPackageByIdResource>(CancellationToken.None);

            // Verify the version list endpoint.
            var versions = await resource.GetAllVersionsAsync(
                "serilog", cache, NullLogger.Instance, CancellationToken.None);

            versions.Should().ContainSingle(v => v == new NuGetVersion("3.1.1"));

            // Verify the nupkg download endpoint.
            using var ms = new MemoryStream();
            var downloaded = await resource.CopyNupkgToStreamAsync(
                "serilog", new NuGetVersion("3.1.1"), ms, cache, NullLogger.Instance, CancellationToken.None);

            downloaded.Should().BeTrue();
            ms.ToArray().Should().BeEquivalentTo(nupkgBytes);

            // Verify that a download event was recorded.
            collector.RecordedEvents.Should().ContainSingle(
                e => e.Identity.Id == "serilog" && e.Identity.Version == "3.1.1");
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
