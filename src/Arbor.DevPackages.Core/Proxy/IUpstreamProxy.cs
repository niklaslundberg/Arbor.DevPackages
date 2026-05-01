using Arbor.DevPackages.Core.Feeds;
using Arbor.DevPackages.Core.Packages;

namespace Arbor.DevPackages.Core.Proxy;

public interface IUpstreamProxy
{
    Task<PackageMetadata?> FetchAndStoreAsync(PackageIdentity identity, FeedConfiguration feed, CancellationToken cancellationToken);
}
