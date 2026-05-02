using System.Net;
using System.Text;
using System.IO.Compression;
using System.Text.Json;
using Arbor.DevPackages.Core.Feeds;
using Arbor.DevPackages.Core.Packages;
using Arbor.DevPackages.Core.Statistics;
using Arbor.DevPackages.Server.FlatContainer;
using Arbor.DevPackages.Server.Registration;
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
using Xunit;

namespace Arbor.DevPackages.Server.Tests.Registration;

public sealed class RegistrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly PackageIdentity TestIdentity = new("Serilog", "3.1.1");

    private static readonly byte[] FakeNupkgBytes = Encoding.UTF8.GetBytes("fake-nupkg");

    private static readonly string TestNuspec =
        "<package><metadata>" +
        "<id>Serilog</id>" +
        "<version>3.1.1</version>" +
        "<authors>Serilog Contributors</authors>" +
        "<description>Simple .NET logging with fully-structured events</description>" +
        "</metadata></package>";

    public RegistrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private WebApplicationFactory<Program> BuildFactory(InMemoryPackageStore? store = null)
    {
        var packageStore = store ?? new InMemoryPackageStore();

        return _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IPackageStore>(packageStore);
                services.AddSingleton<IStatisticsCollector, NoOpTestStatisticsCollector>();
            }));
    }

    private static InMemoryPackageStore StoreWithTestPackage()
    {
        var store = new InMemoryPackageStore();
        store.Add(TestIdentity, FakeNupkgBytes, TestNuspec);
        return store;
    }

    // ─── Registration index ───────────────────────────────────────────────────

    [Fact]
    public async Task GetRegistrationIndex_KnownPackage_ReturnsValidJson()
    {
        using var factory = BuildFactory(StoreWithTestPackage());
        var client = factory.CreateClient();

        var response = await client.GetAsync("/feeds/default/v3/registration/serilog/index.json", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("count").GetInt32().Should().Be(1);

        var items = root.GetProperty("items");
        items.GetArrayLength().Should().Be(1);

        var page = items[0];
        page.GetProperty("count").GetInt32().Should().Be(1);
        page.GetProperty("lower").GetString().Should().Be("3.1.1");
        page.GetProperty("upper").GetString().Should().Be("3.1.1");

        var leafItems = page.GetProperty("items");
        leafItems.GetArrayLength().Should().Be(1);

        var leaf = leafItems[0];
        var catalogEntry = leaf.GetProperty("catalogEntry");
        catalogEntry.GetProperty("id").GetString().Should().Be("serilog");
        catalogEntry.GetProperty("version").GetString().Should().Be("3.1.1");
        catalogEntry.GetProperty("listed").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetRegistrationIndex_UnknownPackage_Returns404()
    {
        using var factory = BuildFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/feeds/default/v3/registration/unknown-package/index.json", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetRegistrationIndex_MultipleVersions_ReturnsSemVerSortedWithCorrectBounds()
    {
        // Add versions deliberately out of order to verify semver sorting.
        var store = new InMemoryPackageStore();
        store.Add(new PackageIdentity("Serilog", "2.0.0"), FakeNupkgBytes, TestNuspec);
        store.Add(new PackageIdentity("Serilog", "1.0.0-beta"), FakeNupkgBytes, TestNuspec);
        store.Add(new PackageIdentity("Serilog", "1.0.0"), FakeNupkgBytes, TestNuspec);

        using var factory = BuildFactory(store);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/feeds/default/v3/registration/serilog/index.json", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);
        var page = doc.RootElement.GetProperty("items")[0];

        // Semver order: 1.0.0-beta < 1.0.0 < 2.0.0
        page.GetProperty("lower").GetString().Should().Be("1.0.0-beta");
        page.GetProperty("upper").GetString().Should().Be("2.0.0");

        var leafItems = page.GetProperty("items");
        leafItems.GetArrayLength().Should().Be(3);
        leafItems[0].GetProperty("catalogEntry").GetProperty("version").GetString().Should().Be("1.0.0-beta");
        leafItems[1].GetProperty("catalogEntry").GetProperty("version").GetString().Should().Be("1.0.0");
        leafItems[2].GetProperty("catalogEntry").GetProperty("version").GetString().Should().Be("2.0.0");
    }

    // ─── Registration leaf ────────────────────────────────────────────────────

    [Fact]
    public async Task GetRegistrationLeaf_KnownPackage_ReturnsVersionMetadata()
    {
        using var factory = BuildFactory(StoreWithTestPackage());
        var client = factory.CreateClient();

        var response = await client.GetAsync("/feeds/default/v3/registration/serilog/3.1.1.json", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("listed").GetBoolean().Should().BeTrue();

        var catalogEntry = root.GetProperty("catalogEntry");
        catalogEntry.GetProperty("id").GetString().Should().Be("serilog");
        catalogEntry.GetProperty("version").GetString().Should().Be("3.1.1");
        catalogEntry.GetProperty("authors").GetString().Should().Be("Serilog Contributors");
        catalogEntry.GetProperty("description").GetString()
            .Should().Be("Simple .NET logging with fully-structured events");
    }

    [Fact]
    public async Task GetRegistrationLeaf_UnknownPackage_Returns404()
    {
        using var factory = BuildFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/feeds/default/v3/registration/unknown-package/1.0.0.json", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── NuGet.Protocol end-to-end ───────────────────────────────────────────

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
    public async Task GetRegistrationIndex_NuGetProtocolClient_CanDeserialize()
    {
        var nupkgBytes = CreateMinimalNupkgBytes();

        var store = new InMemoryPackageStore();
        store.Add(TestIdentity, nupkgBytes, TestNuspec);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        Arbor.DevPackages.ServiceDefaults.Extensions.AddServiceDefaults(builder);
        builder.Services.AddSingleton<IPackageStore>(store);
        builder.Services.AddSingleton<IStatisticsCollector, NoOpTestStatisticsCollector>();
        builder.Services.AddSingleton<IFeedRouter>(
            new Arbor.DevPackages.Core.Feeds.FeedRouter(
                [new FeedConfiguration("default", new Uri("https://api.nuget.org/v3/flatcontainer"))]));

        await using var app = builder.Build();
        Arbor.DevPackages.ServiceDefaults.Extensions.MapDefaultEndpoints(app);
        var feedsGroup = app.MapGroup("/feeds/{feedId}");
        ServiceIndexEndpoints.MapServiceIndex(feedsGroup);
        FlatContainerEndpoints.MapFlatContainer(feedsGroup);
        RegistrationEndpoints.MapRegistration(feedsGroup);

        await app.StartAsync(TestContext.Current.CancellationToken);

        try
        {
            var indexUrl = app.Urls.FirstOrDefault() is { } url
                ? $"{url}/feeds/default/v3/index.json"
                : throw new InvalidOperationException("The test server did not bind to any address.");

            var source = new PackageSource(indexUrl);
            var repository = Repository.Factory.GetCoreV3(source);

            using var cache = new SourceCacheContext { NoCache = true };
            var resource = await repository.GetResourceAsync<PackageMetadataResource>(TestContext.Current.CancellationToken);

            var metadata = await resource.GetMetadataAsync(
                "serilog", includePrerelease: false, includeUnlisted: false,
                cache, NullLogger.Instance, TestContext.Current.CancellationToken);

            var packages = metadata.ToList();
            packages.Should().ContainSingle(package =>
                package.Identity.Id == "serilog" && package.Identity.Version.ToString() == "3.1.1");
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    private sealed class NoOpTestStatisticsCollector : IStatisticsCollector
    {
        public Task RecordDownloadAsync(DownloadEvent downloadEvent, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
