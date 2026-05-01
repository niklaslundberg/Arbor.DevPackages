using Arbor.DevPackages.Core.Statistics;

namespace Arbor.DevPackages.Testing;

/// <summary>
/// A <see cref="IStatisticsCollector"/> fake that captures recorded download events
/// so tests can assert that downloads were properly tracked.
/// </summary>
public sealed class NoOpStatisticsCollector : IStatisticsCollector
{
    private readonly List<DownloadEvent> _events = [];

    /// <summary>
    /// All download events recorded since this instance was created.
    /// </summary>
    public IReadOnlyList<DownloadEvent> RecordedEvents => _events;

    public Task RecordDownloadAsync(DownloadEvent downloadEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(downloadEvent);
        _events.Add(downloadEvent);
        return Task.CompletedTask;
    }
}
