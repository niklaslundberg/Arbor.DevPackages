using Arbor.DevPackages.Core.Feeds;
using Arbor.DevPackages.Core.Packages;
using Arbor.DevPackages.Core.Proxy;
using Arbor.DevPackages.Core.Statistics;
using Arbor.DevPackages.Server.FlatContainer;
using Arbor.DevPackages.Server.Proxy;
using Arbor.DevPackages.Server.Registration;
using Arbor.DevPackages.Server.Search;
using Arbor.DevPackages.Server.ServiceIndex;
using Arbor.DevPackages.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var storePath = builder.Configuration["PackageStorePath"]
    ?? Path.Combine(Path.GetTempPath(), "Arbor.DevPackages", "store");

builder.Services.AddSingleton<IPackageStore>(_ => new FileSystemPackageStore(storePath));
builder.Services.AddSingleton<IStatisticsCollector, NoOpProductionStatisticsCollector>();

// HTTP client factory for upstream proxy.
builder.Services.AddHttpClient();

// Feed configuration (flat-container base URL for the upstream feed).
var upstreamFeedUrl = builder.Configuration["UpstreamFeedUrl"]
    ?? "https://api.nuget.org/v3/flatcontainer";
var upstreamSearchUrlString = builder.Configuration["UpstreamSearchUrl"];
var upstreamSearchUrl = upstreamSearchUrlString is not null ? new Uri(upstreamSearchUrlString) : null;
builder.Services.AddSingleton(new FeedConfiguration("default", new Uri(upstreamFeedUrl), SearchUrl: upstreamSearchUrl));

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
app.MapServiceIndex();
app.MapFlatContainer();
app.MapRegistration();
app.MapSearch();
app.MapSearchCache();

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

