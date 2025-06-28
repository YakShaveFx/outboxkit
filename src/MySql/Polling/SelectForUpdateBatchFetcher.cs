using MySqlConnector;
using YakShaveFx.OutboxKit.Core;
using YakShaveFx.OutboxKit.Core.Polling;
using YakShaveFx.OutboxKit.MySql.Shared;

namespace YakShaveFx.OutboxKit.MySql.Polling;

// ReSharper disable once ClassNeverInstantiated.Global - automagically instantiated by DI
internal sealed class SelectForUpdateBatchFetcher : IBatchFetcher
{
    private delegate BatchContext BatchContextFactory(
        IReadOnlyCollection<IMessage> messages, MySqlConnection connection, MySqlTransaction tx);

    private readonly int _batchSize;
    private readonly string _selectQuery;
    private readonly Func<MySqlDataReader, IMessage> _messageFactory;
    private readonly MySqlDataSource _dataSource;
    private readonly BatchContextFactory _batchContextFactory;

    public SelectForUpdateBatchFetcher(
        MySqlPollingSettings pollingSettings,
        TableConfiguration tableCfg,
        MySqlDataSource dataSource,
        BatchCompleter completer)
    {
        _dataSource = dataSource;
        _batchSize = pollingSettings.BatchSize;
        _selectQuery = SetupSelectQuery(pollingSettings, tableCfg);
        var hasNextQuery = SetupHasNextQuery(pollingSettings, tableCfg);
        _messageFactory = tableCfg.MessageFactory;
        _batchContextFactory =
            (okMessages, connection, transaction) => new BatchContext(
                okMessages,
                connection,
                transaction,
                hasNextQuery,
                completer);
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

    private sealed class BatchContext(
        IReadOnlyCollection<IMessage> messages,
        MySqlConnection connection,
        MySqlTransaction tx,
        string hasNextQuery,
        BatchCompleter completer)
        : IBatchContext
    {
        public IReadOnlyCollection<IMessage> Messages => messages;

        public async Task CompleteAsync(IReadOnlyCollection<IMessage> ok, CancellationToken ct)
        {
            if (ok.Count <= 0)
            {
                await tx.RollbackAsync(ct);
                return;
            }

            await completer.CompleteAsync(ok, connection, tx, ct);
            await tx.CommitAsync(ct);
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

    private static string SetupHasNextQuery(MySqlPollingSettings pollingSettings, TableConfiguration tableCfg) =>
        pollingSettings.CompletionMode == CompletionMode.Delete
            ? $"SELECT EXISTS(SELECT 1 FROM {tableCfg.Name} LIMIT 1);"
            : $"SELECT EXISTS(SELECT 1 FROM {tableCfg.Name} WHERE {tableCfg.ProcessedAtColumn} IS NULL LIMIT 1);";

    private static string SetupSelectQuery(MySqlPollingSettings pollingSettings, TableConfiguration tableCfg) =>
        pollingSettings.CompletionMode == CompletionMode.Delete
            ? $"""
               SELECT {string.Join(", ", tableCfg.Columns)}
               FROM {tableCfg.Name}
               ORDER BY {SetupOrderByClause(tableCfg)}
               LIMIT @size
               FOR UPDATE;
               """
            : $"""
               SELECT {string.Join(", ", tableCfg.Columns)}
               FROM {tableCfg.Name}
               WHERE {tableCfg.ProcessedAtColumn} IS NULL
               ORDER BY {SetupOrderByClause(tableCfg)}
               LIMIT @size
               FOR UPDATE;
               """;

    private static string SetupOrderByClause(TableConfiguration tableCfg) =>
        string.Join(", ", tableCfg.SortExpressions.Select(se => $"{se.Column} {DirectionToString(se.Direction)}"));

    private static string DirectionToString(SortDirection direction) =>
        direction switch
        {
            SortDirection.Ascending => "ASC",
            SortDirection.Descending => "DESC",
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
        };
}