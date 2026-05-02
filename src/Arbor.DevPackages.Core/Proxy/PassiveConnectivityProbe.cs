using System.Collections.Concurrent;
using Arbor.DevPackages.Core.Feeds;

namespace Arbor.DevPackages.Core.Proxy;

/// <summary>
/// Passive connectivity probe: marks an upstream offline on the first fetch failure
/// and allows retries after the configured back-off window elapses.
/// Thread-safe.
/// </summary>
public sealed class PassiveConnectivityProbe : IConnectivityProbe
{
    private readonly ConnectivityProbeOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastFailedAt = new();

    public PassiveConnectivityProbe(ConnectivityProbeOptions options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _options = options;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task<bool> IsReachableAsync(FeedConfiguration feed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(feed);

        if (feed.UpstreamUrl is null)
        {
            return Task.FromResult(false);
        }

        string key = feed.UpstreamUrl.AbsoluteUri;
        if (_lastFailedAt.TryGetValue(key, out DateTimeOffset failedAt))
        {
            TimeSpan elapsed = _timeProvider.GetUtcNow() - failedAt;
            if (elapsed < _options.BackoffDuration)
            {
                return Task.FromResult(false);
            }
        }

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task RecordFailureAsync(FeedConfiguration feed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(feed);

        if (feed.UpstreamUrl is null)
        {
            return Task.CompletedTask;
        }

        string key = feed.UpstreamUrl.AbsoluteUri;
        _lastFailedAt[key] = _timeProvider.GetUtcNow();
        return Task.CompletedTask;
    }
}
