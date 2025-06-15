using Npgsql;
using YakShaveFx.OutboxKit.Core.CleanUp;
using YakShaveFx.OutboxKit.PostgreSql.Shared;

namespace YakShaveFx.OutboxKit.PostgreSql.CleanUp;

internal sealed class Cleaner(
    TableConfiguration tableCfg,
    PostgreSqlCleanUpSettings settings,
    NpgsqlDataSource dataSource,
    TimeProvider timeProvider) : IOutboxCleaner
{
    private readonly string _deleteQuery = $"""
                                            DELETE FROM {tableCfg.Name.Quote()}
                                            WHERE {tableCfg.ProcessedAtColumn.Quote()} < @maxProcessedAt;
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