using Arbor.DevPackages.Core.Feeds;
using Arbor.DevPackages.Core.Proxy;
using AwesomeAssertions;
using Xunit;

namespace Arbor.DevPackages.Core.Tests.Proxy;

public sealed class PassiveConnectivityProbeTests
{
    private static readonly FeedConfiguration Feed =
        new("test", new Uri("https://upstream.example.com/v3/flatcontainer"));

    // ─── RecordFailureAsync + IsReachableAsync ────────────────────────────────

    [Fact]
    public async Task RecordFailureAsync_WhenCalled_IsReachableAsyncReturnsFalse()
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

    // ─── IsReachableAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task IsReachableAsync_WhenWithinBackoffWindow_ReturnsFalse()
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

    [Fact]
    public async Task IsReachableAsync_AfterBackoffExpiry_ReturnsTrue()
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

    [Fact]
    public async Task IsReachableAsync_WithNonUtcOffset_RespectsBackoffWindow()
    {
        // Failure recorded at a non-UTC offset (+02:00); the back-off comparison must
        // be offset-aware and not depend on the local offset being UTC.
        var now = new DateTimeOffset(2026, 5, 1, 14, 0, 0, TimeSpan.FromHours(2)); // 12:00 UTC
        var timeProvider = new ManualTimeProvider(now);
        var options = new ConnectivityProbeOptions { BackoffDuration = TimeSpan.FromSeconds(60) };
        var probe = new PassiveConnectivityProbe(options, timeProvider);

        await probe.RecordFailureAsync(Feed, CancellationToken.None);

        // 30 s later: still within the back-off window regardless of offset.
        timeProvider.Advance(TimeSpan.FromSeconds(30));
        bool withinWindow = await probe.IsReachableAsync(Feed, CancellationToken.None);
        withinWindow.Should().BeFalse("should still be offline within back-off window");

        // 61 s after failure: back-off has elapsed.
        timeProvider.Advance(TimeSpan.FromSeconds(31));
        bool afterExpiry = await probe.IsReachableAsync(Feed, CancellationToken.None);
        afterExpiry.Should().BeTrue("should be reachable after back-off window elapses");
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
