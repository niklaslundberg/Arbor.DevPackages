using Arbor.DevPackages.Core.Packages;
using Arbor.DevPackages.Core.Statistics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Arbor.DevPackages.Core.Retention;

public sealed class RetentionScheduler : BackgroundService, IRetentionScheduler
{
    private readonly IPackageStore _packageStore;
    private readonly IRetentionPolicy _retentionPolicy;
    private readonly IStatisticsReader _statisticsReader;
    private readonly RetentionOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RetentionScheduler> _logger;

    public RetentionScheduler(
        IPackageStore packageStore,
        IRetentionPolicy retentionPolicy,
        IStatisticsReader statisticsReader,
        RetentionOptions options,
        TimeProvider timeProvider,
        ILogger<RetentionScheduler> logger)
    {
        ArgumentNullException.ThrowIfNull(packageStore);
        ArgumentNullException.ThrowIfNull(retentionPolicy);
        ArgumentNullException.ThrowIfNull(statisticsReader);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _packageStore = packageStore;
        _retentionPolicy = retentionPolicy;
        _statisticsReader = statisticsReader;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(_options.SchedulerInterval, stoppingToken);
            await ScheduleAsync(stoppingToken);
        }
    }

    public async Task ScheduleAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset? lastDownloadAt =
            await _statisticsReader.GetLastDownloadedAtAcrossAllPackagesAsync(cancellationToken);

        if (lastDownloadAt is not null)
        {
            TimeSpan timeSinceLastDownload = _timeProvider.GetUtcNow() - lastDownloadAt.Value;
            if (timeSinceLastDownload < _options.InactivityThreshold)
            {
                _logger.LogInformation(
                    "Retention run skipped: last download was {TimeSinceLastDownload:g} ago " +
                    "(threshold: {Threshold:g}).",
                    timeSinceLastDownload,
                    _options.InactivityThreshold);
                return;
            }
        }

        IReadOnlyList<PackageIdentity> allPackages =
            await _packageStore.ListAllAsync(cancellationToken);

        var candidates = new List<(PackageIdentity Identity, string Reason)>();
        foreach (PackageIdentity identity in allPackages)
        {
            RetentionDecision decision =
                await _retentionPolicy.EvaluateAsync(identity, cancellationToken);

            if (decision.Action == RetentionAction.Purge)
            {
                candidates.Add((identity, decision.Reason));
            }
        }

        if (candidates.Count == 0)
        {
            _logger.LogInformation("Retention run complete: no packages eligible for purge.");
            return;
        }

        // Log the full planned purge list before deleting.
        _logger.LogInformation(
            "Retention run: {Count} package(s) scheduled for purge:{NewLine}{Candidates}",
            candidates.Count,
            Environment.NewLine,
            string.Join(Environment.NewLine, candidates.Select(c => $"  {c.Identity.Id} {c.Identity.Version} — {c.Reason}")));

        foreach ((PackageIdentity identity, _) in candidates)
        {
            _logger.LogInformation(
                "Deleting package {Id} {Version}.", identity.Id, identity.Version);
            await _packageStore.DeleteAsync(identity, cancellationToken);
        }

        _logger.LogInformation(
            "Retention run complete: {Count} package(s) purged.", candidates.Count);
    }
}
