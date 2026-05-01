using System.Threading;
using Arbor.DevPackages.Core.Feeds;
using Arbor.DevPackages.Core.Proxy;

namespace Arbor.DevPackages.Testing;

/// <summary>
/// A configurable test double for <see cref="IConnectivityProbe"/>.
/// Thread-safe.
/// </summary>
public sealed class FakeConnectivityProbe : IConnectivityProbe
{
    private readonly bool _isReachable;
    private int _failureCount;

    /// <param name="isReachable">
    /// Whether <see cref="IsReachableAsync"/> should report the upstream as reachable.
    /// </param>
    public FakeConnectivityProbe(bool isReachable = true)
    {
        _isReachable = isReachable;
    }

    /// <summary>
    /// The number of times <see cref="RecordFailureAsync"/> has been called.
    /// </summary>
    public int FailureCount => _failureCount;

    /// <inheritdoc />
    public Task<bool> IsReachableAsync(FeedConfiguration feed, CancellationToken cancellationToken) =>
        Task.FromResult(_isReachable);

    /// <inheritdoc />
    public Task RecordFailureAsync(FeedConfiguration feed, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _failureCount);
        return Task.CompletedTask;
    }
}
