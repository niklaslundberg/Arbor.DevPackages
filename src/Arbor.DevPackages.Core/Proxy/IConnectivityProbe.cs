using Arbor.DevPackages.Core.Feeds;

namespace Arbor.DevPackages.Core.Proxy;

public interface IConnectivityProbe
{
    Task<bool> IsReachableAsync(FeedConfiguration feed, CancellationToken cancellationToken);

    /// <summary>
    /// Records that a fetch attempt for the given feed has failed.
    /// After this call, <see cref="IsReachableAsync"/> will return <c>false</c>
    /// until the configured back-off window has elapsed.
    /// </summary>
    Task RecordFailureAsync(FeedConfiguration feed, CancellationToken cancellationToken);
}
