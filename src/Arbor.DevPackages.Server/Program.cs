using Arbor.DevPackages.Core.Feeds;
using Arbor.DevPackages.Core.Packages;
using Arbor.DevPackages.Core.Proxy;
using Arbor.DevPackages.Core.Statistics;
using Arbor.DevPackages.Server.FlatContainer;
using Arbor.DevPackages.Server.Proxy;
using Arbor.DevPackages.Server.Registration;
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
builder.Services.AddSingleton(new FeedConfiguration("default", new Uri(upstreamFeedUrl)));

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

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapServiceIndex();
app.MapFlatContainer();
app.MapRegistration();

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

