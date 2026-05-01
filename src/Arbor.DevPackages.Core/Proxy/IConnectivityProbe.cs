using Arbor.DevPackages.Core.Feeds;

namespace Arbor.DevPackages.Core.Proxy;

public interface IConnectivityProbe
{
    Task<bool> IsReachableAsync(FeedConfiguration feed, CancellationToken cancellationToken);
}
