using System.Security.Cryptography;
using System.Text;
using MySqlConnector;
using YakShaveFx.OutboxKit.Core;
using YakShaveFx.OutboxKit.Core.Polling;
using YakShaveFx.OutboxKit.MySql.Shared;

namespace YakShaveFx.OutboxKit.MySql.Polling;

// ReSharper disable once ClassNeverInstantiated.Global - automagically instantiated by DI
internal sealed class AdvisoryLockBatchFetcher : IBatchFetcher
{
    private delegate BatchContext BatchContextFactory(
        IReadOnlyCollection<IMessage> messages, MySqlConnection connection);

    private readonly int _batchSize;
    private readonly string _selectQuery;
    private readonly Func<MySqlDataReader, IMessage> _messageFactory;
    private readonly MySqlDataSource _dataSource;
    private readonly BatchContextFactory _batchContextFactory;
    private readonly string _lockName;

    public AdvisoryLockBatchFetcher(
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
            (okMessages, connection) => new BatchContext(
                okMessages,
                connection,
                hasNextQuery,
                completer);

        _lockName = SetupLockName(dataSource);
    }

    private string SetupLockName(MySqlDataSource dataSource)
    {
        // advisory locks are per database server, not per logical database/schema,
        // so creating the lock name based on the logical database name, to avoid conflicts

        const int maxLockNameLength = 64;
        const string prefix = "outboxkit_";
        var databaseName = new MySqlConnectionStringBuilder(dataSource.ConnectionString).Database;

        if (prefix.Length + databaseName.Length <= maxLockNameLength)
        {
            return prefix + databaseName;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(databaseName));
        return string.Concat(
            prefix,
            BitConverter.ToString(hash).Replace("-", "").AsSpan(0, maxLockNameLength - prefix.Length));
    }

    public async Task<IBatchContext> FetchAndHoldAsync(CancellationToken ct)
    {
        var connection = await _dataSource.OpenConnectionAsync(ct);
        try
        {
            if (!await TryAcquireLockAsync(_lockName, connection, ct))
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
        MySqlConnection connection,
        int size,
        CancellationToken ct)
    {
        await using var command = new MySqlCommand(_selectQuery, connection);

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

        public async ValueTask DisposeAsync()
        {
            if (!_lockReleased)
            {
                await ReleaseLockAsync(connection, CancellationToken.None);
            }

            await connection.DisposeAsync();
        }
    }


    private static async Task<bool> TryAcquireLockAsync(string lockName,
        MySqlConnection connection,
        CancellationToken ct)
    {
        var command = new MySqlCommand(
            // lang=mysql
            $"SELECT GET_LOCK('{lockName}', 15);", // 15 seconds timeout
            connection);
        var result = await command.ExecuteScalarAsync(ct);
        return result switch
        {
            bool b => b,
            int i => i == 1,
            long l => l == 1,
            _ => false
        };
    }

    private static async Task ReleaseLockAsync(MySqlConnection connection, CancellationToken ct)
    {
        var command = new MySqlCommand(
            // lang=mysql
            "DO RELEASE_ALL_LOCKS();", // release all locks in the current session
            connection);
        await command.ExecuteNonQueryAsync(ct);
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
               LIMIT @size;
               """
            : $"""
               SELECT {string.Join(", ", tableCfg.Columns)}
               FROM {tableCfg.Name}
               WHERE {tableCfg.ProcessedAtColumn} IS NULL
               ORDER BY {SetupOrderByClause(tableCfg)}
               LIMIT @size;
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