using Arbor.DevPackages.Core.Packages;

namespace Arbor.DevPackages.Core.Statistics;

public sealed record PackageStatsSummary(
    PackageIdentity Identity,
    long DownloadCount,
    DateTimeOffset? LastDownloadedAt);
