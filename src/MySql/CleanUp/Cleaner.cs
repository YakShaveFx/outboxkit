using MySqlConnector;
using YakShaveFx.OutboxKit.Core.CleanUp;
using YakShaveFx.OutboxKit.MySql.Shared;

namespace YakShaveFx.OutboxKit.MySql.CleanUp;

internal sealed class Cleaner(
    TableConfiguration tableCfg,
    MySqlCleanUpSettings settings,
    MySqlDataSource dataSource,
    TimeProvider timeProvider) : IOutboxCleaner
{
    private readonly string _deleteQuery = $"""
                                            DELETE FROM {tableCfg.Name}
                                            WHERE {tableCfg.ProcessedAtColumn} < @maxProcessedAt;
                                            """;

    public async Task<int> CleanAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = _deleteQuery;
        cmd.Parameters.AddWithValue("maxProcessedAt", timeProvider.GetUtcNow().DateTime - settings.MaxAge);
        return await cmd.ExecuteNonQueryAsync(ct);
    }
}