using Arbor.DevPackages.Core.Packages;
using Arbor.DevPackages.Core.Statistics;
using Arbor.DevPackages.Storage.Sqlite.Statistics;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Arbor.DevPackages.Storage.Sqlite.Tests;

public sealed class SqliteStatisticsTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private SqliteStatisticsCollector _collector = null!;
    private SqliteStatisticsReader _reader = null!;

    public async ValueTask InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync(TestContext.Current.CancellationToken);
        await SqliteStatisticsSchema.ApplyAsync(_connection, TestContext.Current.CancellationToken);
        _collector = new SqliteStatisticsCollector(_connection);
        _reader = new SqliteStatisticsReader(_connection);
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task RecordDownload_NewPackage_InsertsRow()
    {
        var identity = new PackageIdentity("Newtonsoft.Json", "13.0.3");
        var timestamp = new DateTimeOffset(2026, 4, 29, 10, 0, 0, TimeSpan.Zero);
        var downloadEvent = new DownloadEvent(identity, timestamp);

        await _collector.RecordDownloadAsync(downloadEvent, TestContext.Current.CancellationToken);

        long count = await _reader.GetDownloadCountAsync(identity, TestContext.Current.CancellationToken);
        count.Should().Be(1);
    }

    [Fact]
    public async Task RecordDownload_SamePackageTwice_InsertsTwoRows()
    {
        var identity = new PackageIdentity("Newtonsoft.Json", "13.0.3");
        var event1 = new DownloadEvent(identity, new DateTimeOffset(2026, 4, 29, 10, 0, 0, TimeSpan.Zero));
        var event2 = new DownloadEvent(identity, new DateTimeOffset(2026, 4, 29, 10, 0, 1, TimeSpan.Zero));

        await _collector.RecordDownloadAsync(event1, TestContext.Current.CancellationToken);
        await _collector.RecordDownloadAsync(event2, TestContext.Current.CancellationToken);

        long count = await _reader.GetDownloadCountAsync(identity, TestContext.Current.CancellationToken);
        count.Should().Be(2);
    }

    [Fact]
    public async Task GetLastDownloadedAt_AfterRecording_ReturnsRecordedTimestamp()
    {
        var identity = new PackageIdentity("Newtonsoft.Json", "13.0.3");
        var timestamp = new DateTimeOffset(2026, 4, 29, 10, 0, 0, TimeSpan.Zero);
        var downloadEvent = new DownloadEvent(identity, timestamp);

        await _collector.RecordDownloadAsync(downloadEvent, TestContext.Current.CancellationToken);

        DateTimeOffset? result = await _reader.GetLastDownloadedAtAsync(identity, TestContext.Current.CancellationToken);
        result.Should().Be(timestamp);
    }

    [Fact]
    public async Task GetLastDownloadedAt_NeverDownloaded_ReturnsNull()
    {
        var identity = new PackageIdentity("SomePackage.NotRecorded", "1.0.0");

        DateTimeOffset? result = await _reader.GetLastDownloadedAtAsync(identity, TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetDownloadCount_AfterThreeDownloads_ReturnsThree()
    {
        var identity = new PackageIdentity("Newtonsoft.Json", "13.0.3");
        var baseTime = new DateTimeOffset(2026, 4, 29, 10, 0, 0, TimeSpan.Zero);

        await _collector.RecordDownloadAsync(new DownloadEvent(identity, baseTime), TestContext.Current.CancellationToken);
        await _collector.RecordDownloadAsync(new DownloadEvent(identity, baseTime.AddSeconds(1)), TestContext.Current.CancellationToken);
        await _collector.RecordDownloadAsync(new DownloadEvent(identity, baseTime.AddSeconds(2)), TestContext.Current.CancellationToken);

        long count = await _reader.GetDownloadCountAsync(identity, TestContext.Current.CancellationToken);
        count.Should().Be(3);
    }

    [Fact]
    public async Task GetLastDownloadAcrossAllPackages_ReturnsLatestTimestamp()
    {
        var identity1 = new PackageIdentity("PackageA", "1.0.0");
        var identity2 = new PackageIdentity("PackageB", "2.0.0");
        var earlier = new DateTimeOffset(2026, 4, 29, 9, 0, 0, TimeSpan.Zero);
        var later = new DateTimeOffset(2026, 4, 29, 10, 0, 0, TimeSpan.Zero);

        await _collector.RecordDownloadAsync(new DownloadEvent(identity1, earlier), TestContext.Current.CancellationToken);
        await _collector.RecordDownloadAsync(new DownloadEvent(identity2, later), TestContext.Current.CancellationToken);

        DateTimeOffset? result = await _reader.GetLastDownloadedAtAcrossAllPackagesAsync(TestContext.Current.CancellationToken);
        result.Should().Be(later);
    }

    [Fact]
    public async Task GetLastDownloadedAt_WithDifferentOffsets_ReturnsLatestUtcInstant()
    {
        var identity = new PackageIdentity("Newtonsoft.Json", "13.0.3");
        // 08:00+02:00 = 06:00 UTC — earlier UTC instant but larger offset string
        var earlierUtc = new DateTimeOffset(2026, 4, 29, 8, 0, 0, TimeSpan.FromHours(2));
        // 08:00+01:00 = 07:00 UTC — later UTC instant
        var laterUtc = new DateTimeOffset(2026, 4, 29, 8, 0, 0, TimeSpan.FromHours(1));

        await _collector.RecordDownloadAsync(new DownloadEvent(identity, earlierUtc), TestContext.Current.CancellationToken);
        await _collector.RecordDownloadAsync(new DownloadEvent(identity, laterUtc), TestContext.Current.CancellationToken);

        DateTimeOffset? result = await _reader.GetLastDownloadedAtAsync(identity, TestContext.Current.CancellationToken);
        result.Should().Be(laterUtc);
    }

    [Fact]
    public async Task GetAllPackageStats_AfterDownloads_ReturnsCountsAndLastTimestamp()
    {
        var identity1 = new PackageIdentity("PackageA", "1.0.0");
        var identity2 = new PackageIdentity("PackageB", "2.0.0");
        var time1 = new DateTimeOffset(2026, 4, 29, 9, 0, 0, TimeSpan.Zero);
        var time2 = new DateTimeOffset(2026, 4, 29, 10, 0, 0, TimeSpan.Zero);
        var time3 = new DateTimeOffset(2026, 4, 29, 11, 0, 0, TimeSpan.Zero);

        await _collector.RecordDownloadAsync(new DownloadEvent(identity1, time1), TestContext.Current.CancellationToken);
        await _collector.RecordDownloadAsync(new DownloadEvent(identity1, time2), TestContext.Current.CancellationToken);
        await _collector.RecordDownloadAsync(new DownloadEvent(identity2, time3), TestContext.Current.CancellationToken);

        IReadOnlyList<PackageStatsSummary> stats = await _reader.GetAllPackageStatsAsync(TestContext.Current.CancellationToken);

        stats.Should().HaveCount(2);

        var a = stats.Single(stat => stat.Identity.Id == "PackageA");
        a.DownloadCount.Should().Be(2);
        a.LastDownloadedAt.Should().Be(time2);

        var b = stats.Single(stat => stat.Identity.Id == "PackageB");
        b.DownloadCount.Should().Be(1);
        b.LastDownloadedAt.Should().Be(time3);
    }

    [Fact]
    public async Task GetAllPackageStats_WithNoDownloads_ReturnsEmpty()
    {
        IReadOnlyList<PackageStatsSummary> stats = await _reader.GetAllPackageStatsAsync(TestContext.Current.CancellationToken);

        stats.Should().BeEmpty();
    }
}
