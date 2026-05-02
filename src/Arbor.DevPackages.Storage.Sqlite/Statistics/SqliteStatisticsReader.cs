using System.Globalization;
using Arbor.DevPackages.Core.Packages;
using Arbor.DevPackages.Core.Statistics;
using Microsoft.Data.Sqlite;

namespace Arbor.DevPackages.Storage.Sqlite.Statistics;

public sealed class SqliteStatisticsReader : IStatisticsReader
{
    private readonly SqliteConnection _connection;

    public SqliteStatisticsReader(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connection = connection;
    }

    public async Task<long> GetDownloadCountAsync(PackageIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);

        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM download_events
            WHERE package_id = $packageId AND version = $version;
            """;
        cmd.Parameters.AddWithValue("$packageId", identity.Id);
        cmd.Parameters.AddWithValue("$version", identity.Version);
        object? result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result);
    }

    public async Task<DateTimeOffset?> GetLastDownloadedAtAsync(PackageIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);

        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT MAX(downloaded_at) FROM download_events
            WHERE package_id = $packageId AND version = $version;
            """;
        cmd.Parameters.AddWithValue("$packageId", identity.Id);
        cmd.Parameters.AddWithValue("$version", identity.Version);
        object? result = await cmd.ExecuteScalarAsync(cancellationToken);
        if (result is null or DBNull)
        {
            return null;
        }

        return ParseTimestamp((string)result);
    }

    public async Task<DateTimeOffset?> GetLastDownloadedAtAcrossAllPackagesAsync(CancellationToken cancellationToken)
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT MAX(downloaded_at) FROM download_events;";
        object? result = await cmd.ExecuteScalarAsync(cancellationToken);
        if (result is null or DBNull)
        {
            return null;
        }

        return ParseTimestamp((string)result);
    }

    public async Task<IReadOnlyList<PackageStatsSummary>> GetAllPackageStatsAsync(CancellationToken cancellationToken)
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT package_id, version, COUNT(*) AS download_count, MAX(downloaded_at) AS last_downloaded_at
            FROM download_events
            GROUP BY package_id, version;
            """;

        using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var results = new List<PackageStatsSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetString(0);
            var version = reader.GetString(1);
            var count = reader.GetInt64(2);
            DateTimeOffset lastDownloadedAt = ParseTimestamp(reader.GetString(3));
            results.Add(new PackageStatsSummary(new PackageIdentity(id, version), count, lastDownloadedAt));
        }

        return results;
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed))
        {
            throw new FormatException($"Stored downloaded_at value '{value}' is not a valid ISO 8601 timestamp.");
        }

        return parsed;
    }
}
