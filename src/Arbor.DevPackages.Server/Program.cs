using Arbor.DevPackages.Core.Feeds;
using Arbor.DevPackages.Core.Packages;
using Arbor.DevPackages.Core.Proxy;
using Arbor.DevPackages.Core.Statistics;
using Arbor.DevPackages.Server.FlatContainer;
using Arbor.DevPackages.Server.Proxy;
using Arbor.DevPackages.Server.Push;
using Arbor.DevPackages.Server.Registration;
using Arbor.DevPackages.Server.Search;
using Arbor.DevPackages.Server.ServiceIndex;
using Arbor.DevPackages.Server.Stats;
using Arbor.DevPackages.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var storePath = builder.Configuration["PackageStorePath"]
    ?? Path.Combine(Path.GetTempPath(), "Arbor.DevPackages", "store");

builder.Services.AddSingleton<IPackageStore>(_ => new FileSystemPackageStore(storePath));
builder.Services.AddSingleton<IStatisticsCollector, NoOpProductionStatisticsCollector>();
builder.Services.AddSingleton<IStatisticsReader, NoOpProductionStatisticsReader>();

// HTTP client factory for upstream proxy.
builder.Services.AddHttpClient();

// Build the list of feed configurations.
// Prefer the structured "Feeds" array; fall back to the legacy flat config keys.
var feedsSection = builder.Configuration.GetSection("Feeds");
List<FeedConfiguration> feeds = [];

if (feedsSection.Exists())
{
    foreach (var section in feedsSection.GetChildren())
    {
        var id = section["Id"] ?? throw new InvalidOperationException("Each feed entry requires an 'Id'.");
        var urlString = section["UpstreamUrl"];
        var upstreamUrl = urlString is not null ? new Uri(urlString) : null;
        var allowPrerelease = section.GetValue<bool>("AllowPrerelease");
        var allowPush = section.GetValue<bool>("AllowPush");
        var searchUrlString = section["SearchUrl"];
        var searchUrl = searchUrlString is not null ? new Uri(searchUrlString) : null;
        feeds.Add(new FeedConfiguration(id, upstreamUrl, AllowPrerelease: allowPrerelease, AllowPush: allowPush, SearchUrl: searchUrl));
    }
}

if (feeds.Count == 0)
{
    // Legacy single-feed configuration fallback.
    // AllowPrerelease defaults to true to preserve the previous behaviour where the
    // client's ?prerelease= query parameter was the sole control.
    var upstreamFeedUrl = builder.Configuration["UpstreamFeedUrl"]
        ?? "https://api.nuget.org/v3/flatcontainer";
    var upstreamSearchUrlString = builder.Configuration["UpstreamSearchUrl"];
    var upstreamSearchUrl = upstreamSearchUrlString is not null ? new Uri(upstreamSearchUrlString) : null;
    feeds.Add(new FeedConfiguration("default", new Uri(upstreamFeedUrl), AllowPrerelease: true, SearchUrl: upstreamSearchUrl));
}

// Register the first feed as a singleton FeedConfiguration so that UpstreamSearchCache
// (which depends on it for its search URL) continues to work without changes.
// TODO: Refactor UpstreamSearchCache to be feed-aware (one cache per feed) and
//       remove this singleton registration (Iteration 11 candidate).
builder.Services.AddSingleton(feeds[0]);
builder.Services.AddSingleton<IFeedRouter>(new FeedRouter(feeds));

// Connectivity probe and upstream proxy.
var backoffSeconds = builder.Configuration.GetValue<double>("ConnectivityProbe:BackoffSeconds");
var probeOptions = backoffSeconds > 0
    ? new ConnectivityProbeOptions { BackoffDuration = TimeSpan.FromSeconds(backoffSeconds) }
    : ConnectivityProbeOptions.Default;
builder.Services.AddSingleton(probeOptions);
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<IConnectivityProbe, PassiveConnectivityProbe>();
builder.Services.AddSingleton<IUpstreamCredentialProvider, NoOpCredentialProvider>();
builder.Services.AddTransient<IUpstreamProxy, UpstreamHttpProxy>();

// Search cache: registered as both IUpstreamSearchCache and a hosted service so
// the same instance is reachable from endpoints and from the background refresh loop.
builder.Services.AddSingleton<UpstreamSearchCache>();
builder.Services.AddSingleton<IUpstreamSearchCache>(sp =>
    sp.GetRequiredService<UpstreamSearchCache>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<UpstreamSearchCache>());

var app = builder.Build();

app.MapDefaultEndpoints();

// All NuGet v3 endpoints are scoped under /feeds/{feedId}.
var feedsGroup = app.MapGroup("/feeds/{feedId}");
feedsGroup.MapServiceIndex();
feedsGroup.MapFlatContainer();
feedsGroup.MapRegistration();
feedsGroup.MapSearch();
feedsGroup.MapPush();

// Admin endpoints (not under /feeds/{feedId}).
app.MapSearchCache();
app.MapStats();

app.Run();

public partial class Program { }

/// <summary>
/// A no-op statistics collector used in production until a persistent implementation is wired up.
/// </summary>
internal sealed class NoOpProductionStatisticsCollector : IStatisticsCollector
{
    public Task RecordDownloadAsync(DownloadEvent downloadEvent, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

/// <summary>
/// A no-op statistics reader used in production until a persistent implementation is wired up.
/// </summary>
internal sealed class NoOpProductionStatisticsReader : IStatisticsReader
{
    public Task<long> GetDownloadCountAsync(PackageIdentity identity, CancellationToken cancellationToken) =>
        Task.FromResult(0L);

    public Task<DateTimeOffset?> GetLastDownloadedAtAsync(PackageIdentity identity, CancellationToken cancellationToken) =>
        Task.FromResult<DateTimeOffset?>(null);

    public Task<DateTimeOffset?> GetLastDownloadedAtAcrossAllPackagesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<DateTimeOffset?>(null);

    public Task<IReadOnlyList<PackageStatsSummary>> GetAllPackageStatsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PackageStatsSummary>>([]);
}

