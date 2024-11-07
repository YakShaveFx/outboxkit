using MySqlConnector;
using YakShaveFx.OutboxKit.Core;
using YakShaveFx.OutboxKit.Core.Polling;

namespace YakShaveFx.OutboxKit.MySql.Polling;

// ReSharper disable once ClassNeverInstantiated.Global - automagically instantiated by DI
internal sealed class OutboxBatchFetcher(
    MySqlPollingSettings pollingSettings,
    TableConfiguration tableCfg,
    MySqlDataSource dataSource)
    : IOutboxBatchFetcher
{
    private readonly int _batchSize = pollingSettings.BatchSize;

    private readonly string _selectQuery = $"""
                                            SELECT {string.Join(", ", tableCfg.Columns)}
                                            FROM {tableCfg.Name}
                                            ORDER BY {tableCfg.OrderByColumn}
                                            LIMIT @size
                                            FOR UPDATE;
                                            """;

    private readonly string _deleteQuery = $"DELETE FROM {tableCfg.Name} WHERE {tableCfg.IdColumn} IN ({{0}});";

    private readonly string _hasNextQuery = $"SELECT EXISTS(SELECT 1 FROM {tableCfg.Name} LIMIT 1);";

    private readonly Func<MySqlDataReader, IMessage> _messageFactory = tableCfg.MessageFactory;
    private readonly Func<IMessage, object> _idGetter = tableCfg.IdGetter;

    public async Task<IOutboxBatchContext> FetchAndHoldAsync(CancellationToken ct)
    {
        var connection = await dataSource.OpenConnectionAsync(ct);
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

            return new BatchContext(messages, connection, tx, _idGetter, _deleteQuery, _hasNextQuery);
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

    private class BatchContext(
        IReadOnlyCollection<IMessage> messages,
        MySqlConnection connection,
        MySqlTransaction tx,
        Func<IMessage, object> idGetter,
        string deleteQuery,
        string hasNextQuery) : IOutboxBatchContext
    {
        public IReadOnlyCollection<IMessage> Messages => messages;

        public async Task CompleteAsync(IReadOnlyCollection<IMessage> ok, CancellationToken ct)
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
}