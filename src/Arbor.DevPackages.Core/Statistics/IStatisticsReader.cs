using Arbor.DevPackages.Core.Packages;

namespace Arbor.DevPackages.Core.Statistics;

public interface IStatisticsReader
{
    Task<long> GetDownloadCountAsync(PackageIdentity identity, CancellationToken cancellationToken);
    Task<DateTimeOffset?> GetLastDownloadedAtAsync(PackageIdentity identity, CancellationToken cancellationToken);
}
