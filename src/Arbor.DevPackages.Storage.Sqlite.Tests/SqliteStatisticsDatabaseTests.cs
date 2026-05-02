using Arbor.DevPackages.Core.Packages;
using Arbor.DevPackages.Core.Statistics;
using Arbor.DevPackages.Storage.Sqlite.Statistics;
using AwesomeAssertions;
using Xunit;

namespace Arbor.DevPackages.Storage.Sqlite.Tests;

public sealed class SqliteStatisticsDatabaseTests
{
    // Use a unique named shared-memory database per test so that
    // all connections opened against the same string share one in-memory store.
    // This avoids the ":memory:" single-connection limitation while keeping
    // tests isolated from each other.
    private static string MakeConnectionString() =>
        $"Data Source=stats_db_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

    [Fact]
    public async Task OpenAsync_WithInMemoryConnectionString_ReturnsInitializedDatabase()
    {
        await using var db = await SqliteStatisticsDatabase.OpenAsync(
            MakeConnectionString(), CancellationToken.None);

        db.Should().NotBeNull();
        db.Collector.Should().NotBeNull();
        db.Reader.Should().NotBeNull();
    }

    [Fact]
    public async Task OpenAsync_AfterOpen_CanRecordAndReadDownloads()
    {
        await using var db = await SqliteStatisticsDatabase.OpenAsync(
            MakeConnectionString(), CancellationToken.None);

        var identity = new PackageIdentity("TestPkg", "1.0.0");
        var timestamp = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);

        await db.Collector.RecordDownloadAsync(new DownloadEvent(identity, timestamp), CancellationToken.None);

        long count = await db.Reader.GetDownloadCountAsync(identity, CancellationToken.None);
        count.Should().Be(1);
    }

    [Fact]
    public async Task OpenAsync_AppliesSchemaMigration_ReadReturnsEmptyBeforeAnyDownloads()
    {
        await using var db = await SqliteStatisticsDatabase.OpenAsync(
            MakeConnectionString(), CancellationToken.None);

        var identity = new PackageIdentity("NeverDownloaded", "2.0.0");

        DateTimeOffset? result = await db.Reader.GetLastDownloadedAtAsync(identity, CancellationToken.None);

        result.Should().BeNull();
    }
}
