using Arbor.DevPackages.Core.Feeds;
using Arbor.DevPackages.Core.Proxy;
using AwesomeAssertions;
using Xunit;

namespace Arbor.DevPackages.Core.Tests.Proxy;

public sealed class PassiveConnectivityProbeTests
{
    private static readonly FeedConfiguration Feed =
        new("test", new Uri("https://upstream.example.com/v3/flatcontainer"));

    // ─── ProbeUpstream_WhenFetchFails_MarksUpstreamOffline ───────────────────

    [Fact]
    public async Task ProbeUpstream_WhenFetchFails_MarksUpstreamOffline()
    {
        var now = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(now);
        var probe = new PassiveConnectivityProbe(ConnectivityProbeOptions.Default, timeProvider);

        // Before any failure — upstream should appear reachable.
        bool beforeFailure = await probe.IsReachableAsync(Feed, CancellationToken.None);
        beforeFailure.Should().BeTrue();

        // Simulate a fetch failure.
        await probe.RecordFailureAsync(Feed, CancellationToken.None);

        // Within the back-off window — upstream should be considered offline.
        bool afterFailure = await probe.IsReachableAsync(Feed, CancellationToken.None);
        afterFailure.Should().BeFalse();
    }

    // ─── ProbeUpstream_WhenOffline_ReturnsOfflineStatus ─────────────────────

    [Fact]
    public async Task ProbeUpstream_WhenOffline_ReturnsOfflineStatus()
    {
        var now = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(now);
        var options = new ConnectivityProbeOptions { BackoffDuration = TimeSpan.FromSeconds(60) };
        var probe = new PassiveConnectivityProbe(options, timeProvider);

        // Record a failure at time T.
        await probe.RecordFailureAsync(Feed, CancellationToken.None);

        // Advance time by 30 seconds — still within the 60-second back-off window.
        timeProvider.Advance(TimeSpan.FromSeconds(30));

        bool reachable = await probe.IsReachableAsync(Feed, CancellationToken.None);
        reachable.Should().BeFalse("upstream should remain offline within the back-off window");
    }

    // ─── ProbeUpstream_AfterBackoffExpiry_AllowsRetry ────────────────────────

    [Fact]
    public async Task ProbeUpstream_AfterBackoffExpiry_AllowsRetry()
    {
        var now = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(now);
        var options = new ConnectivityProbeOptions { BackoffDuration = TimeSpan.FromSeconds(60) };
        var probe = new PassiveConnectivityProbe(options, timeProvider);

        // Record a failure.
        await probe.RecordFailureAsync(Feed, CancellationToken.None);

        // Advance time past the back-off window.
        timeProvider.Advance(TimeSpan.FromSeconds(61));

        bool reachable = await probe.IsReachableAsync(Feed, CancellationToken.None);
        reachable.Should().BeTrue("upstream should be retried after the back-off window elapses");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A <see cref="TimeProvider"/> whose current time can be advanced manually.
    /// </summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public ManualTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

        public void Advance(TimeSpan elapsed) => _utcNow += elapsed;

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
