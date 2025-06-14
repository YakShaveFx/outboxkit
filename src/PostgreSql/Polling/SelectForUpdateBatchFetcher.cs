using Npgsql;
using YakShaveFx.OutboxKit.Core;
using YakShaveFx.OutboxKit.Core.Polling;
using YakShaveFx.OutboxKit.PostgreSql.Shared;

namespace YakShaveFx.OutboxKit.PostgreSql.Polling;

// ReSharper disable once ClassNeverInstantiated.Global - automagically instantiated by DI
internal sealed class SelectForUpdateBatchFetcher : IBatchFetcher
{
    private delegate BatchContext BatchContextFactory(
        IReadOnlyCollection<IMessage> messages, NpgsqlConnection connection, NpgsqlTransaction tx);

    private delegate NpgsqlCommand CompleteCommandFactory(
        IReadOnlyCollection<IMessage> ok, NpgsqlConnection connection, NpgsqlTransaction tx);

    private readonly int _batchSize;
    private readonly string _selectQuery;
    private readonly Func<NpgsqlDataReader, IMessage> _messageFactory;
    private readonly NpgsqlDataSource _dataSource;
    private readonly BatchContextFactory _batchContextFactory;

    public SelectForUpdateBatchFetcher(
        PostgreSqlPollingSettings pollingSettings,
        TableConfiguration tableCfg,
        NpgsqlDataSource dataSource,
        TimeProvider timeProvider)
    {
        _dataSource = dataSource;
        _batchSize = pollingSettings.BatchSize;
        _selectQuery = SetupSelectQuery(pollingSettings, tableCfg);
        var deleteQuery = SetupDeleteQuery(tableCfg);
        var updateQuery = SetupUpdateQuery(tableCfg);
        var hasNextQuery = SetupHasNextQuery(pollingSettings, tableCfg);
        _messageFactory = tableCfg.MessageFactory;
        _batchContextFactory =
            (okMessages, connection, transaction) => new BatchContext(
                okMessages,
                connection,
                transaction,
                hasNextQuery,
                pollingSettings.CompletionMode switch
                {
                    CompletionMode.Delete => (ok, conn, tx) =>
                        CreateDeleteCommand(deleteQuery, tableCfg.IdGetter, ok, conn, tx),
                    CompletionMode.Update => (ok, conn, tx) =>
                        CreateUpdateCommand(updateQuery, timeProvider, tableCfg.IdGetter, ok, conn, tx),
                    _ => throw new InvalidOperationException(
                        $"Invalid completion mode {pollingSettings.CompletionMode}")
                });
    }

    public async Task<IBatchContext> FetchAndHoldAsync(CancellationToken ct)
    {
        var connection = await _dataSource.OpenConnectionAsync(ct);
        try
        {
            var tx = await connection.BeginTransactionAsync(ct);
            var messages = await FetchMessagesAsync(connection, tx, _batchSize, ct);

            if (messages.Count == 0)
            {
                await tx.RollbackAsync(ct);
                await connection.DisposeAsync();
                return EmptyBatchContext.Instance;
            }

            return _batchContextFactory(messages, connection, tx);
        }
        catch (Exception)
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private async Task<List<IMessage>> FetchMessagesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        int size,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(_selectQuery, connection, tx);

        command.Parameters.AddWithValue("size", size);

        await using var reader = await command.ExecuteReaderAsync(ct);

        if (!reader.HasRows) return [];

        var messages = new List<IMessage>(size);
        while (await reader.ReadAsync(ct))
        {
            messages.Add(_messageFactory(reader));
        }

        return messages;
    }

    private sealed class BatchContext(
        IReadOnlyCollection<IMessage> messages,
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        string hasNextQuery,
        CompleteCommandFactory completeCommandFactory)
        : IBatchContext
    {
        public IReadOnlyCollection<IMessage> Messages => messages;

        public async Task CompleteAsync(IReadOnlyCollection<IMessage> ok, CancellationToken ct)
        {
            if (ok.Count > 0)
            {
                await using var command = completeCommandFactory(ok, connection, tx);
                var completed = await command.ExecuteNonQueryAsync(ct);

                if (completed != ok.Count)
                {
                    // think if this is the best way to handle this (considering this shouldn't happen, probably it's good enough)
                    await tx.RollbackAsync(ct);
                    throw new InvalidOperationException("Failed to complete messages");
                }

                await tx.CommitAsync(ct);
            }
            else
            {
                await tx.RollbackAsync(ct);
            }
        }

        public async Task<bool> HasNextAsync(CancellationToken ct)
        {
            var command = new NpgsqlCommand(hasNextQuery, connection);
            var result = await command.ExecuteScalarAsync(ct);
            return result switch
            {
                bool b => b,
                int i => i == 1,
                long l => l == 1,
                _ => false
            };
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    private static NpgsqlCommand CreateDeleteCommand(
        string deleteQuery,
        Func<IMessage, object> idGetter,
        IReadOnlyCollection<IMessage> ok,
        NpgsqlConnection connection,
        NpgsqlTransaction tx)
    {
        var idParams = string.Join(", ", Enumerable.Range(0, ok.Count).Select(i => $"@id{i}"));
        var command = new NpgsqlCommand(string.Format(deleteQuery, idParams), connection, tx);

        var i = 0;
        foreach (var m in ok)
        {
            command.Parameters.AddWithValue($"id{i}", idGetter(m));
            i++;
        }

        return command;
    }

    private static NpgsqlCommand CreateUpdateCommand(
        string updateQuery,
        TimeProvider timeProvider,
        Func<IMessage, object> idGetter,
        IReadOnlyCollection<IMessage> ok,
        NpgsqlConnection connection,
        NpgsqlTransaction tx)
    {
        var idParams = string.Join(", ", Enumerable.Range(0, ok.Count).Select(i => $"@id{i}"));
        var command = new NpgsqlCommand(string.Format(updateQuery, idParams), connection, tx);
        command.Parameters.AddWithValue("processedAt", timeProvider.GetUtcNow().DateTime);

        var i = 0;
        foreach (var m in ok)
        {
            command.Parameters.AddWithValue($"id{i}", idGetter(m));
            i++;
        }

        return command;
    }

    private static string SetupHasNextQuery(PostgreSqlPollingSettings pollingSettings, TableConfiguration tableCfg) =>
        pollingSettings.CompletionMode == CompletionMode.Delete
            ? $"SELECT EXISTS(SELECT 1 FROM {tableCfg.Name.Quote()} LIMIT 1);"
            : $"SELECT EXISTS(SELECT 1 FROM {tableCfg.Name.Quote()} WHERE {tableCfg.ProcessedAtColumn.Quote()} IS NULL LIMIT 1);";

    private static string SetupUpdateQuery(TableConfiguration tableCfg) =>
        $"UPDATE {tableCfg.Name.Quote()} SET {tableCfg.ProcessedAtColumn.Quote()} = @processedAt WHERE {tableCfg.IdColumn.Quote()} IN ({{0}});";

    private static string SetupDeleteQuery(TableConfiguration tableCfg) =>
        $"DELETE FROM {tableCfg.Name.Quote()} WHERE {tableCfg.IdColumn.Quote()} IN ({{0}});";

    private static string SetupSelectQuery(PostgreSqlPollingSettings pollingSettings, TableConfiguration tableCfg) =>
        pollingSettings.CompletionMode == CompletionMode.Delete
            ? $"""
               SELECT {string.Join(", ", tableCfg.Columns.Select(Extensions.Quote))}
               FROM {tableCfg.Name.Quote()}
               ORDER BY {SetupOrderByClause(tableCfg)}
               LIMIT @size
               FOR UPDATE;
               """
            : $"""
               SELECT {string.Join(", ", tableCfg.Columns.Select(Extensions.Quote))}
               FROM {tableCfg.Name.Quote()}
               WHERE {tableCfg.ProcessedAtColumn.Quote()} IS NULL
               ORDER BY {SetupOrderByClause(tableCfg)}
               LIMIT @size
               FOR UPDATE;
               """;

    private static string SetupOrderByClause(TableConfiguration tableCfg) =>
        string.Join(", ", tableCfg.SortExpressions.Select(se => $"{se.Column.Quote()} {DirectionToString(se.Direction)}"));

    private static string DirectionToString(SortDirection direction) =>
        direction switch
        {
            SortDirection.Ascending => "ASC",
            SortDirection.Descending => "DESC",
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
        };
}