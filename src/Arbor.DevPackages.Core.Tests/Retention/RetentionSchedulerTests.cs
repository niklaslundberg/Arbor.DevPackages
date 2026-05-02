using Arbor.DevPackages.Core.Packages;
using Arbor.DevPackages.Core.Retention;
using Arbor.DevPackages.Core.Statistics;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Arbor.DevPackages.Core.Tests.Retention;

public sealed class RetentionSchedulerTests
{
    private static readonly PackageIdentity OldPackage = new("OldLib", "1.0.0");
    private static readonly PackageIdentity RecentPackage = new("RecentLib", "2.0.0");

    private static readonly DateTimeOffset Now = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ScheduleAsync_WhenLastDownloadWithin5Minutes_DoesNotPurge()
    {
        // Last download was 2 minutes ago — within the 5-minute inactivity threshold.
        DateTimeOffset recentDownload = Now.AddMinutes(-2);

        FakePackageStore store = new([OldPackage]);
        FakeRetentionPolicy policy = new(OldPackage, RetentionAction.Purge);
        FakeStatisticsReader stats = new(globalLastDownload: recentDownload);
        FakeLogger<RetentionScheduler> logger = new();

        RetentionScheduler scheduler = new(store, policy, stats, RetentionOptions.Default, new FakeTimeProvider(Now), logger);

        await scheduler.ScheduleAsync(CancellationToken.None);

        store.DeletedPackages.Should().BeEmpty();
    }

    [Fact]
    public async Task ScheduleAsync_WhenLastDownloadOlderThan5Minutes_PurgesEligiblePackages()
    {
        // Last download was 10 minutes ago — older than the 5-minute threshold.
        DateTimeOffset oldDownload = Now.AddMinutes(-10);

        // OldPackage is eligible for purge; RecentPackage is not.
        FakePackageStore store = new([OldPackage, RecentPackage]);
        FakeRetentionPolicy policy = new(OldPackage, RetentionAction.Purge);
        FakeStatisticsReader stats = new(globalLastDownload: oldDownload);
        FakeLogger<RetentionScheduler> logger = new();

        RetentionScheduler scheduler = new(store, policy, stats, RetentionOptions.Default, new FakeTimeProvider(Now), logger);

        await scheduler.ScheduleAsync(CancellationToken.None);

        store.DeletedPackages.Should().ContainSingle().Which.Should().Be(OldPackage);
    }

    [Fact]
    public async Task ScheduleAsync_LogsPurgeCandidatesBeforeDeleting()
    {
        DateTimeOffset oldDownload = Now.AddMinutes(-10);

        FakePackageStore store = new([OldPackage]);
        FakeRetentionPolicy policy = new(OldPackage, RetentionAction.Purge);
        FakeStatisticsReader stats = new(globalLastDownload: oldDownload);
        FakeLogger<RetentionScheduler> logger = new();

        RetentionScheduler scheduler = new(store, policy, stats, RetentionOptions.Default, new FakeTimeProvider(Now), logger);

        await scheduler.ScheduleAsync(CancellationToken.None);

        // A log entry listing purge candidates must appear before any deletion.
        int purgeListLogIndex = logger.Messages.FindIndex(
            m => m.Contains("scheduled for purge", StringComparison.OrdinalIgnoreCase));
        int deleteLogIndex = logger.Messages.FindIndex(
            m => m.Contains("Deleting package", StringComparison.OrdinalIgnoreCase));

        purgeListLogIndex.Should().BeGreaterThanOrEqualTo(0, "purge candidates should be logged");
        deleteLogIndex.Should().BeGreaterThanOrEqualTo(0, "deletion should be logged");
        purgeListLogIndex.Should().BeLessThan(deleteLogIndex,
            "purge list should be logged before individual deletions");
    }

    // --- Fakes ---

    private sealed class FakePackageStore : IPackageStore
    {
        private readonly List<PackageIdentity> _packages;
        public List<PackageIdentity> DeletedPackages { get; } = [];

        public FakePackageStore(IEnumerable<PackageIdentity> packages)
        {
            _packages = [..packages];
        }

        public Task<IReadOnlyList<PackageIdentity>> ListAllAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PackageIdentity>>(_packages);

        public Task DeleteAsync(PackageIdentity identity, CancellationToken cancellationToken)
        {
            DeletedPackages.Add(identity);
            return Task.CompletedTask;
        }

        public Task<PackageMetadata?> GetMetadataAsync(PackageIdentity identity, CancellationToken cancellationToken)
            => Task.FromResult<PackageMetadata?>(null);

        public Task<Stream?> OpenNupkgAsync(PackageIdentity identity, CancellationToken cancellationToken)
            => Task.FromResult<Stream?>(null);

        public Task<Stream?> OpenNuspecAsync(PackageIdentity identity, CancellationToken cancellationToken)
            => Task.FromResult<Stream?>(null);

        public Task<PackageStoreResult> StoreAsync(PackageIdentity identity, Stream nupkg, Stream nuspec, CancellationToken cancellationToken)
            => Task.FromResult(PackageStoreResult.Stored);

        public Task<string?> GetStoredHashAsync(PackageIdentity identity, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

        public Task<bool> ExistsAsync(PackageIdentity identity, CancellationToken cancellationToken)
            => Task.FromResult(_packages.Contains(identity));
    }

    private sealed class FakeRetentionPolicy : IRetentionPolicy
    {
        private readonly PackageIdentity _purgeTarget;
        private readonly RetentionAction _purgeAction;

        public FakeRetentionPolicy(PackageIdentity purgeTarget, RetentionAction purgeAction)
        {
            _purgeTarget = purgeTarget;
            _purgeAction = purgeAction;
        }

        public Task<RetentionDecision> EvaluateAsync(PackageIdentity identity, CancellationToken cancellationToken)
        {
            RetentionAction action = identity == _purgeTarget ? _purgeAction : RetentionAction.Keep;
            string reason = action == RetentionAction.Purge ? "eligible" : "recently downloaded";
            return Task.FromResult(new RetentionDecision(action, reason));
        }
    }

    private sealed class FakeStatisticsReader : IStatisticsReader
    {
        private readonly DateTimeOffset? _globalLastDownload;

        public FakeStatisticsReader(DateTimeOffset? globalLastDownload)
        {
            _globalLastDownload = globalLastDownload;
        }

        public Task<long> GetDownloadCountAsync(PackageIdentity identity, CancellationToken cancellationToken)
            => Task.FromResult(0L);

        public Task<DateTimeOffset?> GetLastDownloadedAtAsync(PackageIdentity identity, CancellationToken cancellationToken)
            => Task.FromResult<DateTimeOffset?>(null);

        public Task<DateTimeOffset?> GetLastDownloadedAtAcrossAllPackagesAsync(CancellationToken cancellationToken)
            => Task.FromResult(_globalLastDownload);

        public Task<IReadOnlyList<PackageStatsSummary>> GetAllPackageStatsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PackageStatsSummary>>([]);
    }

    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakeLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
