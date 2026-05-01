using Arbor.DevPackages.Core.Feeds;

namespace Arbor.DevPackages.Core.Proxy;

public interface IUpstreamCredentialProvider
{
    Task<string?> GetCredentialAsync(FeedConfiguration feed, CancellationToken cancellationToken);
}
