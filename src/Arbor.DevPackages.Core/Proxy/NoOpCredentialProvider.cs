using Arbor.DevPackages.Core.Feeds;

namespace Arbor.DevPackages.Core.Proxy;

/// <summary>
/// A no-op credential provider that always returns <c>null</c>.
/// Used in scenarios where the upstream feed does not require authentication.
/// </summary>
public sealed class NoOpCredentialProvider : IUpstreamCredentialProvider
{
    public Task<string?> GetCredentialAsync(FeedConfiguration feed, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(null);
}
