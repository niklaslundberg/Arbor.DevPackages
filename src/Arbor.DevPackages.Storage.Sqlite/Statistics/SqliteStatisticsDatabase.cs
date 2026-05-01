using Arbor.DevPackages.Core.Statistics;
using Microsoft.Data.Sqlite;

namespace Arbor.DevPackages.Storage.Sqlite.Statistics;

/// <summary>
/// Convenience factory that opens a SQLite connection, applies the schema migration,
/// and exposes ready-to-use <see cref="IStatisticsCollector"/> and <see cref="IStatisticsReader"/>
/// instances. Use this as the production initialization path.
/// </summary>
public sealed class SqliteStatisticsDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public IStatisticsCollector Collector { get; }
    public IStatisticsReader Reader { get; }

    private SqliteStatisticsDatabase(SqliteConnection connection)
    {
        _connection = connection;
        Collector = new SqliteStatisticsCollector(connection);
        Reader = new SqliteStatisticsReader(connection);
    }

    public static async Task<SqliteStatisticsDatabase> OpenAsync(
        string connectionString, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connectionString);

        SqliteConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        await SqliteStatisticsSchema.ApplyAsync(connection, cancellationToken);
        return new SqliteStatisticsDatabase(connection);
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
}
