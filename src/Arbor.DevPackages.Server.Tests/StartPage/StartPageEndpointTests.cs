using System.Net;
using Arbor.DevPackages.Core.Feeds;
using Arbor.DevPackages.Core.Packages;
using Arbor.DevPackages.Core.Statistics;
using Arbor.DevPackages.Server.StartPage;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Arbor.DevPackages.Server.Tests.StartPage;

public sealed class StartPageEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public StartPageEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // ─── GetStartPage_ReturnsOkWithHtmlContentType ─────────────────────────────

    [Fact]
    public async Task GetStartPage_ReturnsOkWithHtmlContentType()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
    }

    // ─── GetStartPage_ContainsFeedServiceIndexUrl ──────────────────────────────

    [Fact]
    public async Task GetStartPage_ContainsFeedServiceIndexUrl()
    {
        var feedId = "myfeed";
        var upstreamUrl = new Uri("https://api.nuget.org/v3/index.json");
        var feed = new FeedConfiguration(feedId, upstreamUrl, AllowPrerelease: true, AllowPush: false);

        using var factory = _factory.WithWebHostBuilder(b =>
            b.ConfigureServices(services =>
            {
                services.AddSingleton<IReadOnlyList<FeedConfiguration>>([feed]);
                services.AddSingleton<IFeedRouter>(new Arbor.DevPackages.Core.Feeds.FeedRouter([feed]));
            }));

        var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        html.Should().Contain($"/feeds/{feedId}/v3/index.json");
        html.Should().Contain(feedId);
    }

    // ─── GetStartPage_ContainsUpstreamUrl ─────────────────────────────────────

    [Fact]
    public async Task GetStartPage_ContainsUpstreamUrl()
    {
        var upstreamUrl = new Uri("https://api.nuget.org/v3/index.json");
        var feed = new FeedConfiguration("myproxy", upstreamUrl);

        using var factory = _factory.WithWebHostBuilder(b =>
            b.ConfigureServices(services =>
            {
                services.AddSingleton<IReadOnlyList<FeedConfiguration>>([feed]);
                services.AddSingleton<IFeedRouter>(new Arbor.DevPackages.Core.Feeds.FeedRouter([feed]));
            }));

        var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        html.Should().Contain(upstreamUrl.ToString());
    }

    // ─── GetStartPage_WithNoDownloads_ShowsNoDownloadsMessage ─────────────────

    [Fact]
    public async Task GetStartPage_WithNoDownloads_ShowsNoDownloadsMessage()
    {
        using var factory = _factory.WithWebHostBuilder(b =>
            b.ConfigureServices(services =>
            {
                services.AddSingleton<IStatisticsReader>(new FakeStatisticsReader([]));
            }));

        var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        html.Should().Contain("No downloads recorded yet.");
    }

    // ─── GetStartPage_WithDownloads_ShowsStatisticsTable ──────────────────────

    [Fact]
    public async Task GetStartPage_WithDownloads_ShowsStatisticsTable()
    {
        var identity = new PackageIdentity("Newtonsoft.Json", "13.0.3");
        var lastDownloadedAt = new DateTimeOffset(2026, 4, 29, 10, 0, 0, TimeSpan.Zero);
        var summary = new PackageStatsSummary(identity, DownloadCount: 7, LastDownloadedAt: lastDownloadedAt);

        using var factory = _factory.WithWebHostBuilder(b =>
            b.ConfigureServices(services =>
            {
                services.AddSingleton<IStatisticsReader>(new FakeStatisticsReader([summary]));
            }));

        var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        html.Should().Contain("Newtonsoft.Json");
        html.Should().Contain("13.0.3");
        html.Should().Contain("7");
    }

    // ─── BuildHtml_WithMultipleFeeds_ContainsAllFeedIds ───────────────────────

    [Fact]
    public void BuildHtml_WithMultipleFeeds_ContainsAllFeedIds()
    {
        var feeds = new List<FeedConfiguration>
        {
            new FeedConfiguration("alpha", new Uri("https://alpha.example.com/v3/index.json")),
            new FeedConfiguration("beta", new Uri("https://beta.example.com/v3/index.json")),
        };

        var html = StartPageEndpoints.BuildHtml(feeds, [], "http://localhost:5000");

        html.Should().Contain("/feeds/alpha/v3/index.json");
        html.Should().Contain("/feeds/beta/v3/index.json");
    }

    // ─── BuildHtml_HtmlEncodesSpecialCharactersInFeedId ───────────────────────

    [Fact]
    public void BuildHtml_HtmlEncodesSpecialCharactersInFeedId()
    {
        // Feed ID with characters that need HTML-encoding (< > &)
        // Note: FeedRouter rejects '/' '?' '#' '%'; '&' '<' '>' are valid in a feed ID
        // but must be HTML-encoded in the output.
        var feed = new FeedConfiguration("a&b", null);
        var html = StartPageEndpoints.BuildHtml([feed], [], "http://localhost:5000");

        html.Should().Contain("a&amp;b");
        html.Should().NotContain("<script");
    }

    // ─── BuildHtml_LocalFeedWithNullUpstream_DoesNotShowUpstreamSection ───────

    [Fact]
    public void BuildHtml_LocalFeedWithNullUpstream_DoesNotShowUpstreamSection()
    {
        var feed = new FeedConfiguration("local", AllowPush: true);
        var html = StartPageEndpoints.BuildHtml([feed], [], "http://localhost:5000");

        html.Should().Contain("local");
        html.Should().NotContain("Upstream:");
    }

    // ─── BuildHtml_NoFeeds_ShowsNoFeedsMessage ────────────────────────────────

    [Fact]
    public void BuildHtml_NoFeeds_ShowsNoFeedsMessage()
    {
        var html = StartPageEndpoints.BuildHtml([], [], "http://localhost:5000");

        html.Should().Contain("No feeds configured.");
    }

    // ─── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeStatisticsReader : IStatisticsReader
    {
        private readonly IReadOnlyList<PackageStatsSummary> _summaries;

        public FakeStatisticsReader(IReadOnlyList<PackageStatsSummary> summaries) =>
            _summaries = summaries;

        public Task<long> GetDownloadCountAsync(PackageIdentity identity, CancellationToken cancellationToken) =>
            Task.FromResult(0L);

        public Task<DateTimeOffset?> GetLastDownloadedAtAsync(PackageIdentity identity, CancellationToken cancellationToken) =>
            Task.FromResult<DateTimeOffset?>(null);

        public Task<DateTimeOffset?> GetLastDownloadedAtAcrossAllPackagesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<DateTimeOffset?>(null);

        public Task<IReadOnlyList<PackageStatsSummary>> GetAllPackageStatsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_summaries);
    }
}
