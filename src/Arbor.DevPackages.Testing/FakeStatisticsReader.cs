using Arbor.DevPackages.Core.Packages;
using Arbor.DevPackages.Core.Statistics;

namespace Arbor.DevPackages.Testing;

/// <summary>
/// A configurable <see cref="IStatisticsReader"/> test double that returns a fixed list
/// of <see cref="PackageStatsSummary"/> records. Shared across test projects to avoid
/// duplication.
/// </summary>
public sealed class FakeStatisticsReader : IStatisticsReader
{
    private readonly IReadOnlyList<PackageStatsSummary> _summaries;

    public FakeStatisticsReader(IReadOnlyList<PackageStatsSummary> summaries)
    {
        ArgumentNullException.ThrowIfNull(summaries);
        _summaries = summaries;
    }

    public Task<long> GetDownloadCountAsync(PackageIdentity identity, CancellationToken cancellationToken) =>
        Task.FromResult(0L);

    public Task<DateTimeOffset?> GetLastDownloadedAtAsync(PackageIdentity identity, CancellationToken cancellationToken) =>
        Task.FromResult<DateTimeOffset?>(null);

    public Task<DateTimeOffset?> GetLastDownloadedAtAcrossAllPackagesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<DateTimeOffset?>(null);

    public Task<IReadOnlyList<PackageStatsSummary>> GetAllPackageStatsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_summaries);
}
