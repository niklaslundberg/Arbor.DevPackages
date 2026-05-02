using Arbor.DevPackages.Core.Feeds;
using Arbor.DevPackages.Core.Packages;
using Arbor.DevPackages.Core.Proxy;

namespace Arbor.DevPackages.Testing;

/// <summary>
/// A configurable test double for <see cref="IUpstreamProxy"/>.
/// Behaviour is supplied via a delegate at construction time.
/// </summary>
public sealed class FakeUpstreamProxy : IUpstreamProxy
{
    private readonly Func<PackageIdentity, FeedConfiguration, CancellationToken, Task<PackageMetadata?>> _handler;

    /// <param name="handler">
    /// Called by <see cref="FetchAndStoreAsync"/>. Return <c>null</c> to simulate
    /// "package not found upstream"; throw to simulate an upstream error.
    /// </param>
    public FakeUpstreamProxy(
        Func<PackageIdentity, FeedConfiguration, CancellationToken, Task<PackageMetadata?>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
    }

    /// <inheritdoc />
    public Task<PackageMetadata?> FetchAndStoreAsync(
        PackageIdentity identity,
        FeedConfiguration feed,
        CancellationToken cancellationToken) =>
        _handler(identity, feed, cancellationToken);
}
