using Arbor.DevPackages.Core.Packages;
using Arbor.DevPackages.Core.Statistics;

namespace Arbor.DevPackages.Core.Retention;

public sealed class AgeBasedRetentionPolicy : IRetentionPolicy
{
    private readonly RetentionOptions _options;
    private readonly IStatisticsReader _statisticsReader;
    private readonly TimeProvider _timeProvider;

    public AgeBasedRetentionPolicy(
        RetentionOptions options,
        IStatisticsReader statisticsReader,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(statisticsReader);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _options = options;
        _statisticsReader = statisticsReader;
        _timeProvider = timeProvider;
    }

    public async Task<RetentionDecision> EvaluateAsync(
        PackageIdentity identity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);

        DateTimeOffset? lastDownloadedAt =
            await _statisticsReader.GetLastDownloadedAtAsync(identity, cancellationToken);

        if (lastDownloadedAt is null)
        {
            return new RetentionDecision(RetentionAction.Purge,
                $"Package {identity.Id} {identity.Version} has never been downloaded.");
        }

        DateTimeOffset cutoff = _timeProvider.GetUtcNow() - _options.RetentionWindow;
        if (lastDownloadedAt.Value < cutoff)
        {
            return new RetentionDecision(RetentionAction.Purge,
                $"Package {identity.Id} {identity.Version} was last downloaded at " +
                $"{lastDownloadedAt.Value:O}, which is before the cutoff {cutoff:O}.");
        }

        return new RetentionDecision(RetentionAction.Keep,
            $"Package {identity.Id} {identity.Version} was last downloaded at " +
            $"{lastDownloadedAt.Value:O}, which is within the retention window.");
    }
}
