using Npgsql;
using YakShaveFx.OutboxKit.Core;
using YakShaveFx.OutboxKit.Core.Polling;
using YakShaveFx.OutboxKit.PostgreSql.Shared;

namespace YakShaveFx.OutboxKit.PostgreSql.Polling;

// ReSharper disable once ClassNeverInstantiated.Global - automagically instantiated by DI
internal sealed class AdvisoryLockBatchFetcher : IBatchFetcher
{
    private delegate BatchContext BatchContextFactory(
        IReadOnlyCollection<IMessage> messages, NpgsqlConnection connection);

    private readonly int _batchSize;
    private readonly string _selectQuery;
    private readonly Func<NpgsqlDataReader, IMessage> _messageFactory;
    private readonly NpgsqlDataSource _dataSource;
    private readonly BatchContextFactory _batchContextFactory;
    private readonly AdvisoryLockKey _lockKey;

    public AdvisoryLockBatchFetcher(
        PostgreSqlPollingSettings pollingSettings,
        TableConfiguration tableCfg,
        NpgsqlDataSource dataSource,
        BatchCompleter completer)
    {
        _dataSource = dataSource;
        _batchSize = pollingSettings.BatchSize;
        _selectQuery = SetupSelectQuery(pollingSettings, tableCfg);
        var hasNextQuery = SetupHasNextQuery(pollingSettings, tableCfg);
        _messageFactory = tableCfg.MessageFactory;
        _batchContextFactory =
            (okMessages, connection) => new BatchContext(
                okMessages,
                connection,
                hasNextQuery,
                completer);

        _lockKey = SetupLockKey(dataSource);
    }

    private static AdvisoryLockKey SetupLockKey(NpgsqlDataSource dataSource)
    {
        // advisory locks are per database server, not per logical database/schema,
        // so creating the lock key based on the logical database name, to minimize conflicts
        var databaseName = new NpgsqlConnectionStringBuilder(dataSource.ConnectionString).Database!;
        return new AdvisoryLockKey("outboxkit".GetHashCode(), databaseName.GetHashCode());
    }

    public async Task<IBatchContext> FetchAndHoldAsync(CancellationToken ct)
    {
        var connection = await _dataSource.OpenConnectionAsync(ct);
        try
        {
            if (!await TryAcquireLockAsync(_lockKey, connection, ct))
            {
                await connection.DisposeAsync();
                return EmptyBatchContext.Instance;
            }

            var messages = await FetchMessagesAsync(connection, _batchSize, ct);

            if (messages.Count == 0)
            {
                await ReleaseLockAsync(connection, ct);
                await connection.DisposeAsync();
                return EmptyBatchContext.Instance;
            }

            return _batchContextFactory(messages, connection);
        }
        catch (Exception)
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private async Task<List<IMessage>> FetchMessagesAsync(
        NpgsqlConnection connection,
        int size,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(_selectQuery, connection);

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
        string hasNextQuery,
        BatchCompleter completer)
        : IBatchContext
    {
        private bool _lockReleased;

        public IReadOnlyCollection<IMessage> Messages => messages;

        public async Task CompleteAsync(IReadOnlyCollection<IMessage> ok, CancellationToken ct)
        {
            if (ok.Count <= 0) return;

            await completer.CompleteAsync(ok, connection, null, ct);
            await ReleaseLockAsync(connection, ct); // release immediately, to allow other fetchers to proceed
            _lockReleased = true;
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

        public async ValueTask DisposeAsync()
        {
            if (!_lockReleased)
            {
                await ReleaseLockAsync(connection, CancellationToken.None);
            }

            await connection.DisposeAsync();
        }
    }

    private static async Task<bool> TryAcquireLockAsync(
        AdvisoryLockKey lockKey,
        NpgsqlConnection connection,
        CancellationToken ct)
    {
        // lang=postgresql
        var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@lockKey1, @lockKey2);", connection);
        command.Parameters.AddWithValue("lockKey1", lockKey.Key1);
        command.Parameters.AddWithValue("lockKey2", lockKey.Key2);
        var result = await command.ExecuteScalarAsync(ct);
        return result switch
        {
            bool b => b,
            int i => i == 1,
            long l => l == 1,
            _ => false
        };
    }

    private static async Task ReleaseLockAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        var command = new NpgsqlCommand(
            // lang=postgresql
            "SELECT pg_advisory_unlock_all();", // release all locks in the current session
            connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static string SetupHasNextQuery(PostgreSqlPollingSettings pollingSettings, TableConfiguration tableCfg) =>
        pollingSettings.CompletionMode == CompletionMode.Delete
            ? $"SELECT EXISTS(SELECT 1 FROM {tableCfg.Name.Quote()} LIMIT 1);"
            : $"SELECT EXISTS(SELECT 1 FROM {tableCfg.Name.Quote()} WHERE {tableCfg.ProcessedAtColumn.Quote()} IS NULL LIMIT 1);";

    private static string SetupSelectQuery(PostgreSqlPollingSettings pollingSettings, TableConfiguration tableCfg) =>
        pollingSettings.CompletionMode == CompletionMode.Delete
            ? $"""
               SELECT {string.Join(", ", tableCfg.Columns.Select(Extensions.Quote))}
               FROM {tableCfg.Name.Quote()}
               ORDER BY {SetupOrderByClause(tableCfg)}
               LIMIT @size;
               """
            : $"""
               SELECT {string.Join(", ", tableCfg.Columns.Select(Extensions.Quote))}
               FROM {tableCfg.Name.Quote()}
               WHERE {tableCfg.ProcessedAtColumn.Quote()} IS NULL
               ORDER BY {SetupOrderByClause(tableCfg)}
               LIMIT @size;
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
    
    private sealed record AdvisoryLockKey(int Key1, int Key2);
}
