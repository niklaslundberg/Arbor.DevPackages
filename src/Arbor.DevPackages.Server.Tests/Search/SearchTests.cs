using System.Net;
using System.Text.Json;
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
        return _factory.WithWebHostBuilder(b =>
            b.ConfigureServices(services =>
            {
                services.AddSingleton(cache);
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

        var response = await client.GetAsync("/v3/search?q=serilog");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
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

        var response = await client.GetAsync("/v3/search?prerelease=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("totalHits").GetInt32().Should().Be(2);

        var ids = doc.RootElement.GetProperty("data")
            .EnumerateArray()
            .Select(e => e.GetProperty("id").GetString())
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
        var response = await client.GetAsync("/v3/search?prerelease=false");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("totalHits").GetInt32().Should().Be(1);

        var ids = doc.RootElement.GetProperty("data")
            .EnumerateArray()
            .Select(e => e.GetProperty("id").GetString())
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

        var response = await client.GetAsync("/v3/search?prerelease=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("totalHits").GetInt32().Should().BeGreaterThan(0);

        var ids = doc.RootElement.GetProperty("data")
            .EnumerateArray()
            .Select(e => e.GetProperty("id").GetString())
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
            "/api/feeds/default/search-cache/refresh", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
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
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        Extensions.AddServiceDefaults(builder);
        builder.Services.AddSingleton<IUpstreamSearchCache>(new FakeUpstreamSearchCache(entries));

        await using var app = builder.Build();
        Extensions.MapDefaultEndpoints(app);
        ServiceIndexEndpoints.MapServiceIndex(app);
        SearchEndpoints.MapSearch(app);

        await app.StartAsync();

        try
        {
            var indexUrl = app.Urls.FirstOrDefault() is { } url
                ? $"{url}/v3/index.json"
                : throw new InvalidOperationException("The test server did not bind to any address.");

            var source = new PackageSource(indexUrl);
            var repository = Repository.Factory.GetCoreV3(source);

            var resource = await repository.GetResourceAsync<PackageSearchResource>(CancellationToken.None);

            var results = await resource.SearchAsync(
                "Serilog",
                new SearchFilter(includePrerelease: false),
                skip: 0,
                take: 10,
                NullLogger.Instance,
                CancellationToken.None);

            var packages = results.ToList();
            packages.Should().ContainSingle(p =>
                p.Identity.Id.Equals("Serilog", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
