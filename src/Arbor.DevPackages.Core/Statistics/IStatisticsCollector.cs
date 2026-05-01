namespace Arbor.DevPackages.Core.Statistics;

public interface IStatisticsCollector
{
    Task RecordDownloadAsync(DownloadEvent downloadEvent, CancellationToken cancellationToken);
}
