using Microsoft.Data.Sqlite;

namespace Arbor.DevPackages.Storage.Sqlite.Statistics;

public static class SqliteStatisticsSchema
{
    public static async Task ApplyAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS download_events (
                id             INTEGER PRIMARY KEY AUTOINCREMENT,
                package_id     TEXT    NOT NULL,
                version        TEXT    NOT NULL,
                downloaded_at  TEXT    NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_download_events_pkg
                ON download_events (package_id, version, downloaded_at);
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
