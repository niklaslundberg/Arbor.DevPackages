using Arbor.DevPackages.Core.Statistics;
using Microsoft.Data.Sqlite;

namespace Arbor.DevPackages.Storage.Sqlite.Statistics;

public sealed class SqliteStatisticsCollector : IStatisticsCollector
{
    private readonly SqliteConnection _connection;

    public SqliteStatisticsCollector(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connection = connection;
    }

    public async Task RecordDownloadAsync(DownloadEvent downloadEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(downloadEvent);

        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO download_events (package_id, version, downloaded_at)
            VALUES ($packageId, $version, $downloadedAt);
            """;
        cmd.Parameters.AddWithValue("$packageId", downloadEvent.Identity.Id);
        cmd.Parameters.AddWithValue("$version", downloadEvent.Identity.Version);
        cmd.Parameters.AddWithValue("$downloadedAt", downloadEvent.DownloadedAt.UtcDateTime.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
