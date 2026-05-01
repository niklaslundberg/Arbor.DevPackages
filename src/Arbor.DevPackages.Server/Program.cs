using Arbor.DevPackages.Core.Packages;
using Arbor.DevPackages.Core.Statistics;
using Arbor.DevPackages.Server.FlatContainer;
using Arbor.DevPackages.Server.ServiceIndex;
using Arbor.DevPackages.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var storePath = builder.Configuration["PackageStorePath"]
    ?? Path.Combine(Path.GetTempPath(), "Arbor.DevPackages", "store");

builder.Services.AddSingleton<IPackageStore>(_ => new FileSystemPackageStore(storePath));
builder.Services.AddSingleton<IStatisticsCollector, NoOpProductionStatisticsCollector>();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapServiceIndex();
app.MapFlatContainer();

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
