using System.Net;
using System.Text.Json;
using Arbor.DevPackages.Core.Feeds;
using Arbor.DevPackages.Server.Search;
using Arbor.DevPackages.Server.ServiceIndex;
using Arbor.DevPackages.ServiceDefaults;
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

namespace Arbor.DevPackages.Server.Tests.Search;

public sealed class SearchTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SearchTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private WebApplicationFactory<Program> BuildFactory(IUpstreamSearchCache cache)
    {
        return _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(cache);
            }));
    }

    private WebApplicationFactory<Program> BuildFactory(
        IUpstreamSearchCache cache,
        IFeedRouter feedRouter)
    {
        return _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(cache);
                services.AddSingleton(feedRouter);
            }));
    }

    private static SearchResultPackage BuildPackage(
        string id,
        string version,
        string? description = null,
        IReadOnlyList<SearchVersionEntry>? versions = null)
    {
        var versionEntries = versions ??
            [new SearchVersionEntry(null, version, 0)];

        return new SearchResultPackage(
            AtId: null,
            Id: id,
            Version: version,
            Description: description,
            Summary: null,
            Title: id,
            IconUrl: null,
            LicenseUrl: null,
            ProjectUrl: null,
            Tags: null,
            Authors: null,
            TotalDownloads: 0,
            Verified: false,
            Versions: versionEntries);
    }

    private sealed class FakeUpstreamSearchCache : IUpstreamSearchCache
    {
        private readonly IReadOnlyList<SearchResultPackage>? _entries;

        public FakeUpstreamSearchCache(IReadOnlyList<SearchResultPackage>? entries) =>
            _entries = entries;

        public IReadOnlyList<SearchResultPackage>? GetAllEntries() => _entries;

        public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    // ─── Search_WithQuery_ReturnsMatchingPackages ─────────────────────────────

    [Fact]
    public async Task Search_WithQuery_ReturnsMatchingPackages()
    {
        var entries = new[]
        {
            BuildPackage("Serilog", "3.1.1"),
            BuildPackage("Newtonsoft.Json", "13.0.3")
        };

        using var factory = BuildFactory(new FakeUpstreamSearchCache(entries));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/feeds/default/v3/search?q=serilog", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);

        var totalHits = doc.RootElement.GetProperty("totalHits").GetInt32();
        totalHits.Should().Be(1);

        var data = doc.RootElement.GetProperty("data");
        data.GetArrayLength().Should().Be(1);
        data[0].GetProperty("id").GetString().Should().Be("Serilog");
    }

    // ─── Search_WithPrereleaseTrue_IncludesPrereleasePackages ─────────────────

    [Fact]
    public async Task Search_WithPrereleaseTrue_IncludesPrereleasePackages()
    {
        var entries = new[]
        {
            BuildPackage("Serilog", "3.1.1"),
            BuildPackage(
                "Serilog.Sinks.File",
                "6.0.0-beta.1",
                versions: [new SearchVersionEntry(null, "6.0.0-beta.1", 0)])
        };

        using var factory = BuildFactory(new FakeUpstreamSearchCache(entries));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/feeds/default/v3/search?prerelease=true", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("totalHits").GetInt32().Should().Be(2);

        var ids = doc.RootElement.GetProperty("data")
            .EnumerateArray()
            .Select(element => element.GetProperty("id").GetString())
            .ToList();

        ids.Should().Contain("Serilog.Sinks.File");
    }

    // ─── Search_WithPrereleaseFalse_ExcludesPrereleasePackages ───────────────

    [Fact]
    public async Task Search_WithPrereleaseFalse_ExcludesPrereleasePackages()
    {
        var entries = new[]
        {
            BuildPackage("Serilog", "3.1.1"),
            BuildPackage(
                "Serilog.Sinks.File",
                "6.0.0-beta.1",
                versions: [new SearchVersionEntry(null, "6.0.0-beta.1", 0)])
        };

        using var factory = BuildFactory(new FakeUpstreamSearchCache(entries));
        var client = factory.CreateClient();

        // prerelease=false is the default; include it explicitly for clarity.
        var response = await client.GetAsync("/feeds/default/v3/search?prerelease=false", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("totalHits").GetInt32().Should().Be(1);

        var ids = doc.RootElement.GetProperty("data")
            .EnumerateArray()
            .Select(element => element.GetProperty("id").GetString())
            .ToList();

        ids.Should().NotContain("Serilog.Sinks.File");
        ids.Should().Contain("Serilog");
    }

    // ─── Search_WhenUpstreamOffline_ReturnsLastCachedResults ─────────────────

    [Fact]
    public async Task Search_WhenUpstreamOffline_ReturnsLastCachedResults()
    {
        // Simulate a cache that has been previously populated (stale results retained
        // from before the upstream went offline).
        var cachedEntries = new[] { BuildPackage("Serilog", "3.1.1") };
        using var factory = BuildFactory(new FakeUpstreamSearchCache(cachedEntries));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/feeds/default/v3/search?prerelease=true", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("totalHits").GetInt32().Should().BeGreaterThan(0);

        var ids = doc.RootElement.GetProperty("data")
            .EnumerateArray()
            .Select(element => element.GetProperty("id").GetString())
            .ToList();

        ids.Should().Contain("Serilog");
    }

    // ─── SearchCacheRefresh_Returns200 ────────────────────────────────────────

    [Fact]
    public async Task SearchCacheRefresh_Returns200()
    {
        using var factory = BuildFactory(new FakeUpstreamSearchCache([]));
        var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/api/feeds/default/search-cache/refresh", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ─── Feed-aware AllowPrerelease filtering ─────────────────────────────────

    [Fact]
    public async Task Search_FeedWithAllowPrereleaseTrue_IncludesPrerelease()
    {
        var entries = new[]
        {
            BuildPackage("Serilog", "3.1.1"),
            BuildPackage(
                "Serilog.Sinks.File",
                "6.0.0-beta.1",
                versions: [new SearchVersionEntry(null, "6.0.0-beta.1", 0)])
        };

        // Override feed router with a feed that explicitly allows prerelease.
        var feedRouter = new Arbor.DevPackages.Core.Feeds.FeedRouter(
            [new FeedConfiguration("default", new Uri("https://api.nuget.org/v3/flatcontainer"), AllowPrerelease: true)]);

        using var factory = BuildFactory(new FakeUpstreamSearchCache(entries), feedRouter);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/feeds/default/v3/search?prerelease=true", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("totalHits").GetInt32().Should().Be(2);

        var ids = doc.RootElement.GetProperty("data")
            .EnumerateArray()
            .Select(element => element.GetProperty("id").GetString())
            .ToList();

        ids.Should().Contain("Serilog.Sinks.File");
        ids.Should().Contain("Serilog");
    }

    [Fact]
    public async Task Search_FeedWithAllowPrereleaseFalse_ExcludesPrerelease()
    {
        var entries = new[]
        {
            BuildPackage("Serilog", "3.1.1"),
            BuildPackage(
                "Serilog.Sinks.File",
                "6.0.0-beta.1",
                versions: [new SearchVersionEntry(null, "6.0.0-beta.1", 0)])
        };

        // Override feed router with a feed that disallows prerelease.
        var feedRouter = new Arbor.DevPackages.Core.Feeds.FeedRouter(
            [new FeedConfiguration("default", new Uri("https://api.nuget.org/v3/flatcontainer"), AllowPrerelease: false)]);

        using var factory = BuildFactory(new FakeUpstreamSearchCache(entries), feedRouter);
        var client = factory.CreateClient();

        // Even though the client requests prerelease=true, the feed disallows it.
        var response = await client.GetAsync("/feeds/default/v3/search?prerelease=true", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("totalHits").GetInt32().Should().Be(1);

        var ids = doc.RootElement.GetProperty("data")
            .EnumerateArray()
            .Select(element => element.GetProperty("id").GetString())
            .ToList();

        ids.Should().NotContain("Serilog.Sinks.File");
        ids.Should().Contain("Serilog");
    }

    // ─── NuGet.Protocol end-to-end ───────────────────────────────────────────

    [Fact]
    public async Task Search_NuGetProtocolClient_CanSearchPackages()
    {
        var entries = new SearchResultPackage[]
        {
            BuildPackage("Serilog", "3.1.1", "Simple .NET logging"),
            BuildPackage("Newtonsoft.Json", "13.0.3", "Popular JSON framework")
        };

        // Start a real Kestrel listener so NuGet.Protocol uses its own HTTP stack.
        // ContentRootPath is set to a unique, empty temp directory so no ambient
        // appsettings.json can override UseUrls or add unexpected Kestrel config.
        var isolatedContentRoot = Directory.CreateTempSubdirectory("ArborDevPkgTest_").FullName;
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = isolatedContentRoot });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            Extensions.AddServiceDefaults(builder);
            builder.Services.AddSingleton<IUpstreamSearchCache>(new FakeUpstreamSearchCache(entries));
            builder.Services.AddSingleton<IFeedRouter>(
                new Arbor.DevPackages.Core.Feeds.FeedRouter(
                    [new FeedConfiguration("default", new Uri("https://api.nuget.org/v3/flatcontainer"))]));

            await using var app = builder.Build();
            Extensions.MapDefaultEndpoints(app);
            var feedsGroup = app.MapGroup("/feeds/{feedId}");
            ServiceIndexEndpoints.MapServiceIndex(feedsGroup);
            SearchEndpoints.MapSearch(feedsGroup);

            await app.StartAsync(TestContext.Current.CancellationToken);

            try
            {
                var indexUrl = app.Urls.FirstOrDefault() is { } url
                    ? $"{url}/feeds/default/v3/index.json"
                    : throw new InvalidOperationException("The test server did not bind to any address.");

                var source = new PackageSource(indexUrl);
                var repository = Repository.Factory.GetCoreV3(source);

                var resource = await repository.GetResourceAsync<PackageSearchResource>(TestContext.Current.CancellationToken);

                var results = await resource.SearchAsync(
                    "Serilog",
                    new SearchFilter(includePrerelease: false),
                    skip: 0,
                    take: 10,
                    NullLogger.Instance,
                    TestContext.Current.CancellationToken);

                var packages = results.ToList();
                packages.Should().ContainSingle(package =>
                    package.Identity.Id.Equals("Serilog", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                await app.StopAsync(TestContext.Current.CancellationToken);
            }
        }
        finally
        {
            Directory.Delete(isolatedContentRoot, recursive: true);
        }
    }

    // ─── Edge cases ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_MatchByDescription_ReturnsMatchingPackage()
    {
        var entries = new[]
        {
            BuildPackage("Serilog", "3.1.1", description: "Simple structured logging"),
            BuildPackage("Newtonsoft.Json", "13.0.3", description: "JSON serialization framework")
        };

        using var factory = BuildFactory(new FakeUpstreamSearchCache(entries));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/feeds/default/v3/search?q=structured+logging", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);

        var ids = doc.RootElement.GetProperty("data")
            .EnumerateArray()
            .Select(element => element.GetProperty("id").GetString())
            .ToList();

        ids.Should().Contain("Serilog");
        ids.Should().NotContain("Newtonsoft.Json");
    }

    [Fact]
    public async Task Search_PackageWithNoVersionsList_StableTopLevelVersion_IsIncludedWithoutPrerelease()
    {
        // Package has no Versions list — only a top-level Version string.
        // When it is stable, it should pass the HasStableVersion check.
        var entries = new[]
        {
            new SearchResultPackage(
                AtId: null, Id: "SomePkg", Version: "1.0.0",
                Description: null, Summary: null, Title: "SomePkg",
                IconUrl: null, LicenseUrl: null, ProjectUrl: null,
                Tags: null, Authors: null, TotalDownloads: 0, Verified: false,
                Versions: null)
        };

        using var factory = BuildFactory(new FakeUpstreamSearchCache(entries));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/feeds/default/v3/search?prerelease=false", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("totalHits").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("data")[0].GetProperty("id").GetString().Should().Be("SomePkg");
    }

    [Fact]
    public async Task Search_PackageWithNoVersionsList_PrereleaseTopLevelVersion_IsExcludedWithoutPrerelease()
    {
        // Package has no Versions list and a pre-release top-level version.
        // Without prerelease=true it should be excluded.
        var entries = new[]
        {
            new SearchResultPackage(
                AtId: null, Id: "BetaPkg", Version: "1.0.0-beta.1",
                Description: null, Summary: null, Title: "BetaPkg",
                IconUrl: null, LicenseUrl: null, ProjectUrl: null,
                Tags: null, Authors: null, TotalDownloads: 0, Verified: false,
                Versions: null)
        };

        using var factory = BuildFactory(new FakeUpstreamSearchCache(entries));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/feeds/default/v3/search?prerelease=false", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("totalHits").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Search_WhenCacheHasNullEntries_ReturnsEmpty()
    {
        // GetAllEntries() returns null — treated as an empty list.
        using var factory = BuildFactory(new FakeUpstreamSearchCache(entries: null));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/feeds/default/v3/search", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("totalHits").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Search_UnknownFeed_Returns404()
    {
        using var factory = BuildFactory(new FakeUpstreamSearchCache([]));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/feeds/nonexistent/v3/search", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Search_WithSkipAndTake_ReturnsPaginatedSubset()
    {
        var entries = Enumerable.Range(1, 10)
            .Select(index => BuildPackage($"Package{index:D2}", "1.0.0"))
            .ToArray();

        using var factory = BuildFactory(new FakeUpstreamSearchCache(entries));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/feeds/default/v3/search?skip=3&take=4", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);

        // totalHits reflects the total matching count (10), not the page size.
        doc.RootElement.GetProperty("totalHits").GetInt32().Should().Be(10);
        doc.RootElement.GetProperty("data").GetArrayLength().Should().Be(4);
    }
}
