using System.Net;
using System.Text.Json;
using Arbor.DevPackages.Core.Packages;
using Arbor.DevPackages.Core.Statistics;
using Arbor.DevPackages.Testing;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Arbor.DevPackages.Server.Tests.Stats;

public sealed class StatsEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public StatsEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private WebApplicationFactory<Program> BuildFactory(IStatisticsReader reader)
    {
        return _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(reader);
            }));
    }

    // ─── GetStats_ReturnsDownloadCountAndLastTimestamp ────────────────────────

    [Fact]
    public async Task GetStats_ReturnsDownloadCountAndLastTimestamp()
    {
        var identity = new PackageIdentity("Newtonsoft.Json", "13.0.3");
        var lastDownloadedAt = new DateTimeOffset(2026, 4, 29, 10, 0, 0, TimeSpan.Zero);
        var summary = new PackageStatsSummary(identity, DownloadCount: 42, LastDownloadedAt: lastDownloadedAt);

        using var factory = BuildFactory(new FakeStatisticsReader([summary]));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/stats", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);

        var packages = doc.RootElement.GetProperty("packages");
        packages.GetArrayLength().Should().Be(1);

        var package = packages[0];
        package.GetProperty("id").GetString().Should().Be("Newtonsoft.Json");
        package.GetProperty("version").GetString().Should().Be("13.0.3");
        package.GetProperty("downloadCount").GetInt64().Should().Be(42);
        package.GetProperty("lastDownloadedAt").GetDateTimeOffset().Should().Be(lastDownloadedAt);
    }

    // ─── GetStats_WithNoDownloads_ReturnsEmptyArray ───────────────────────────

    [Fact]
    public async Task GetStats_WithNoDownloads_ReturnsEmptyArray()
    {
        using var factory = BuildFactory(new FakeStatisticsReader([]));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/stats", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);

        var packages = doc.RootElement.GetProperty("packages");
        packages.GetArrayLength().Should().Be(0);
    }

    // ─── Fakes ────────────────────────────────────────────────────────────────
}
