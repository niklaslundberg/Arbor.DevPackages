using Arbor.DevPackages.Core.Packages;

namespace Arbor.DevPackages.Core.Statistics;

public sealed record DownloadEvent(PackageIdentity Identity, DateTimeOffset DownloadedAt);
