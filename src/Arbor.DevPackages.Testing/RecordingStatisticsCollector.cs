using System.Collections.Concurrent;
using Arbor.DevPackages.Core.Statistics;

namespace Arbor.DevPackages.Testing;

/// <summary>
/// A <see cref="IStatisticsCollector"/> test double that captures recorded download events
/// so tests can assert that downloads were properly tracked.
/// Thread-safe: safe for concurrent use across parallel ASP.NET request handlers.
/// </summary>
public sealed class RecordingStatisticsCollector : IStatisticsCollector
{
    private readonly ConcurrentQueue<DownloadEvent> _events = new();

    /// <summary>
    /// A snapshot of all download events recorded since this instance was created.
    /// </summary>
    public IReadOnlyList<DownloadEvent> RecordedEvents => _events.ToArray();

    public Task RecordDownloadAsync(DownloadEvent downloadEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(downloadEvent);
        _events.Enqueue(downloadEvent);
        return Task.CompletedTask;
    }
}
