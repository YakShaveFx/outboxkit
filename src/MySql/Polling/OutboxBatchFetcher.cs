using MySqlConnector;
using YakShaveFx.OutboxKit.Core;
using YakShaveFx.OutboxKit.Core.Polling;
using YakShaveFx.OutboxKit.MySql.Shared;

namespace YakShaveFx.OutboxKit.MySql.Polling;

// ReSharper disable once ClassNeverInstantiated.Global - automagically instantiated by DI
internal sealed class OutboxBatchFetcher : IOutboxBatchFetcher
{
    private delegate BatchContextBase BatchContextFactory(
        IReadOnlyCollection<IMessage> messages,
        MySqlConnection connection,
        MySqlTransaction tx);

    private readonly int _batchSize;
    private readonly string _selectQuery;
    private readonly Func<MySqlDataReader, IMessage> _messageFactory;
    private readonly MySqlDataSource _dataSource;
    private readonly BatchContextFactory _batchContextFactory;

    public OutboxBatchFetcher(
        MySqlPollingSettings pollingSettings,
        TableConfiguration tableCfg,
        MySqlDataSource dataSource,
        TimeProvider timeProvider)
    {
        _dataSource = dataSource;
        _batchSize = pollingSettings.BatchSize;
        _selectQuery = SetupSelectQuery(pollingSettings, tableCfg);
        var deleteQuery = SetupDeleteQuery(tableCfg);
        var updateQuery = SetupUpdateQuery(tableCfg);
        var hasNextQuery = SetupHasNextQuery(pollingSettings, tableCfg);
        _messageFactory = tableCfg.MessageFactory;
        _batchContextFactory = pollingSettings.CompletionMode switch
        {
            CompletionMode.Delete => (messages, connection, tx) =>
                new BatchContextWithDelete(messages, connection, tx, tableCfg.IdGetter, deleteQuery, hasNextQuery),
            CompletionMode.Update => (messages, connection, tx) =>
                new BatchContextWithUpdate(
                    messages,
                    connection,
                    tx,
                    tableCfg.IdGetter,
                    timeProvider,
                    updateQuery,
                    hasNextQuery),
            _ => throw new ArgumentOutOfRangeException(nameof(pollingSettings.CompletionMode))
        };
    }

    public async Task<IOutboxBatchContext> FetchAndHoldAsync(CancellationToken ct)
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
        MySqlConnection connection,
        MySqlTransaction tx,
        int size,
        CancellationToken ct)
    {
        await using var command = new MySqlCommand(_selectQuery, connection, tx);

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

    private abstract class BatchContextBase(
        IReadOnlyCollection<IMessage> messages,
        MySqlConnection connection,
        string hasNextQuery) : IOutboxBatchContext
    {
        public IReadOnlyCollection<IMessage> Messages => messages;

        public abstract Task CompleteAsync(IReadOnlyCollection<IMessage> ok, CancellationToken ct);

        public async Task<bool> HasNextAsync(CancellationToken ct)
        {
            var command = new MySqlCommand(hasNextQuery, connection);
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

    private class BatchContextWithDelete(
        IReadOnlyCollection<IMessage> messages,
        MySqlConnection connection,
        MySqlTransaction tx,
        Func<IMessage, object> idGetter,
        string deleteQuery,
        string hasNextQuery) : BatchContextBase(messages, connection, hasNextQuery)
    {
        public override async Task CompleteAsync(IReadOnlyCollection<IMessage> ok, CancellationToken ct)
        {
            if (ok.Count > 0)
            {
                var idParams = string.Join(", ", Enumerable.Range(0, ok.Count).Select(i => $"@id{i}"));
                var command = new MySqlCommand(string.Format(deleteQuery, idParams), connection, tx);

                var i = 0;
                foreach (var m in ok)
                {
                    command.Parameters.AddWithValue($"id{i}", idGetter(m));
                    i++;
                }

                var deleted = await command.ExecuteNonQueryAsync(ct);

                if (deleted != ok.Count)
                {
                    // think if this is the best way to handle this (considering this shouldn't happen, probably it's good enough)
                    await tx.RollbackAsync(ct);
                    throw new InvalidOperationException("Failed to delete messages");
                }

                await tx.CommitAsync(ct);
            }
            else
            {
                await tx.RollbackAsync(ct);
            }
        }
    }

    private class BatchContextWithUpdate(
        IReadOnlyCollection<IMessage> messages,
        MySqlConnection connection,
        MySqlTransaction tx,
        Func<IMessage, object> idGetter,
        TimeProvider timeProvider,
        string updateQuery,
        string hasNextQuery) : BatchContextBase(messages, connection, hasNextQuery)
    {
        public override async Task CompleteAsync(IReadOnlyCollection<IMessage> ok, CancellationToken ct)
        {
            if (ok.Count > 0)
            {
                var idParams = string.Join(", ", Enumerable.Range(0, ok.Count).Select(i => $"@id{i}"));
                var command = new MySqlCommand(string.Format(updateQuery, idParams), connection, tx);
                command.Parameters.AddWithValue("processedAt", timeProvider.GetUtcNow().DateTime);

                var i = 0;
                foreach (var m in ok)
                {
                    command.Parameters.AddWithValue($"id{i}", idGetter(m));
                    i++;
                }

                var updated = await command.ExecuteNonQueryAsync(ct);

                if (updated != ok.Count)
                {
                    // think if this is the best way to handle this (considering this shouldn't happen, probably it's good enough)
                    await tx.RollbackAsync(ct);
                    throw new InvalidOperationException("Failed to set messages as processed");
                }

                await tx.CommitAsync(ct);
            }
            else
            {
                await tx.RollbackAsync(ct);
            }
        }
    }

    private static string SetupHasNextQuery(MySqlPollingSettings pollingSettings, TableConfiguration tableCfg) =>
        pollingSettings.CompletionMode == CompletionMode.Delete
            ? $"SELECT EXISTS(SELECT 1 FROM {tableCfg.Name} LIMIT 1);"
            : $"SELECT EXISTS(SELECT 1 FROM {tableCfg.Name} WHERE {tableCfg.ProcessedAtColumn} IS NULL LIMIT 1);";

    private static string SetupUpdateQuery(TableConfiguration tableCfg) =>
        $"UPDATE {tableCfg.Name} SET {tableCfg.ProcessedAtColumn} = @processedAt WHERE {tableCfg.IdColumn} IN ({{0}});";

    private static string SetupDeleteQuery(TableConfiguration tableCfg) =>
        $"DELETE FROM {tableCfg.Name} WHERE {tableCfg.IdColumn} IN ({{0}});";

    private static string SetupSelectQuery(MySqlPollingSettings pollingSettings, TableConfiguration tableCfg) =>
        pollingSettings.CompletionMode == CompletionMode.Delete
            ? $"""
               SELECT {string.Join(", ", tableCfg.Columns)}
               FROM {tableCfg.Name}
               ORDER BY {tableCfg.OrderByColumn}
               LIMIT @size
               FOR UPDATE;
               """
            : $"""
               SELECT {string.Join(", ", tableCfg.Columns)}
               FROM {tableCfg.Name}
               WHERE {tableCfg.ProcessedAtColumn} IS NULL
               ORDER BY {tableCfg.OrderByColumn}
               LIMIT @size
               FOR UPDATE;
               """;
}