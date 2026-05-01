using Arbor.DevPackages.Core.Packages;
using Arbor.DevPackages.Core.Retention;
using Arbor.DevPackages.Core.Statistics;
using AwesomeAssertions;
using Xunit;

namespace Arbor.DevPackages.Core.Tests.Retention;

public sealed class AgeBasedRetentionPolicyTests
{
    private static readonly PackageIdentity Package = new("Serilog", "3.1.1");

    [Fact]
    public async Task EvaluateAsync_PackageOlderThan30Days_IsEligible()
    {
        DateTimeOffset now = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset lastDownload = now.AddDays(-31);

        FakeStatisticsReader stats = new(Package, lastDownload);
        AgeBasedRetentionPolicy policy = new(RetentionOptions.Default, stats, new FakeTimeProvider(now));

        RetentionDecision decision = await policy.EvaluateAsync(Package, CancellationToken.None);

        decision.Action.Should().Be(RetentionAction.Purge);
    }

    [Fact]
    public async Task EvaluateAsync_PackageDownloadedToday_IsNotEligible()
    {
        DateTimeOffset now = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset lastDownload = now.AddHours(-1);

        FakeStatisticsReader stats = new(Package, lastDownload);
        AgeBasedRetentionPolicy policy = new(RetentionOptions.Default, stats, new FakeTimeProvider(now));

        RetentionDecision decision = await policy.EvaluateAsync(Package, CancellationToken.None);

        decision.Action.Should().Be(RetentionAction.Keep);
    }

    [Fact]
    public async Task EvaluateAsync_NeverDownloadedPackage_IsEligible()
    {
        DateTimeOffset now = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

        FakeStatisticsReader stats = new(Package, lastDownloadedAt: null);
        AgeBasedRetentionPolicy policy = new(RetentionOptions.Default, stats, new FakeTimeProvider(now));

        RetentionDecision decision = await policy.EvaluateAsync(Package, CancellationToken.None);

        decision.Action.Should().Be(RetentionAction.Purge);
    }

    [Fact]
    public async Task EvaluateAsync_PackageOlderThan30Days_WithNonUtcOffset_IsEligible()
    {
        // lastDownloadedAt is expressed in +02:00 but the UTC instant is 31 days before now.
        DateTimeOffset now = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        // Construct a DateTimeOffset in +02:00: local time is 2 hours ahead, so UTC instant = local - 2h.
        // We want UTC = now - 31 days, so local = (now - 31 days) + 2h.
        TimeSpan offset = TimeSpan.FromHours(2);
        DateTime localTime = DateTime.SpecifyKind((now - TimeSpan.FromDays(31) + offset).DateTime, DateTimeKind.Unspecified);
        DateTimeOffset lastDownload = new(localTime, offset);

        FakeStatisticsReader stats = new(Package, lastDownload);
        AgeBasedRetentionPolicy policy = new(RetentionOptions.Default, stats, new FakeTimeProvider(now));

        RetentionDecision decision = await policy.EvaluateAsync(Package, CancellationToken.None);

        decision.Action.Should().Be(RetentionAction.Purge);
    }

    [Fact]
    public async Task EvaluateAsync_PackageDownloadedToday_WithNonUtcOffset_IsNotEligible()
    {
        // lastDownloadedAt is expressed in +02:00 but the UTC instant is only 1 hour before now.
        DateTimeOffset now = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        TimeSpan offset = TimeSpan.FromHours(2);
        DateTime localTime = DateTime.SpecifyKind((now - TimeSpan.FromHours(1) + offset).DateTime, DateTimeKind.Unspecified);
        DateTimeOffset lastDownload = new(localTime, offset);

        FakeStatisticsReader stats = new(Package, lastDownload);
        AgeBasedRetentionPolicy policy = new(RetentionOptions.Default, stats, new FakeTimeProvider(now));

        RetentionDecision decision = await policy.EvaluateAsync(Package, CancellationToken.None);

        decision.Action.Should().Be(RetentionAction.Keep);
    }

    // --- Fakes ---

    private sealed class FakeStatisticsReader : IStatisticsReader
    {
        private readonly PackageIdentity _trackedPackage;
        private readonly DateTimeOffset? _lastDownloadedAt;

        public FakeStatisticsReader(PackageIdentity trackedPackage, DateTimeOffset? lastDownloadedAt)
        {
            _trackedPackage = trackedPackage;
            _lastDownloadedAt = lastDownloadedAt;
        }

        public Task<long> GetDownloadCountAsync(PackageIdentity identity, CancellationToken cancellationToken)
            => Task.FromResult(0L);

        public Task<DateTimeOffset?> GetLastDownloadedAtAsync(PackageIdentity identity, CancellationToken cancellationToken)
        {
            DateTimeOffset? result = identity == _trackedPackage ? _lastDownloadedAt : null;
            return Task.FromResult(result);
        }

        public Task<DateTimeOffset?> GetLastDownloadedAtAcrossAllPackagesAsync(CancellationToken cancellationToken)
            => Task.FromResult(_lastDownloadedAt);
    }

    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
