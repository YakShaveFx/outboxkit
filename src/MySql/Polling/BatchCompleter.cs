using MySqlConnector;
using YakShaveFx.OutboxKit.Core;
using YakShaveFx.OutboxKit.Core.Polling;
using YakShaveFx.OutboxKit.MySql.Shared;

namespace YakShaveFx.OutboxKit.MySql.Polling;

// ReSharper disable once ClassNeverInstantiated.Global - automagically instantiated by DI
internal sealed class BatchCompleter : IBatchCompleteRetrier
{
    private delegate MySqlCommand CompleteCommandFactory(
        IReadOnlyCollection<IMessage> ok, MySqlConnection connection, MySqlTransaction? tx);

    private delegate MySqlCommand CheckLeftBehindCommandFactory(
        IReadOnlyCollection<IMessage> messages,
        MySqlConnection connection);

    private readonly MySqlDataSource _dataSource;
    private readonly CompleteCommandFactory _completeCommandFactory;
    private readonly CheckLeftBehindCommandFactory _checkLeftBehindCommandFactory;

    public BatchCompleter(
        MySqlPollingSettings pollingSettings,
        TableConfiguration tableCfg,
        MySqlDataSource dataSource,
        TimeProvider timeProvider)
    {
        _dataSource = dataSource;
        var deleteQuery = SetupDeleteQuery(tableCfg);
        var updateQuery = SetupUpdateQuery(tableCfg);
        _completeCommandFactory = pollingSettings.CompletionMode switch
        {
            CompletionMode.Delete => (ok, conn, tx) =>
                CreateDeleteCommand(deleteQuery, tableCfg.IdGetter, ok, conn, tx),
            CompletionMode.Update => (ok, conn, tx) =>
                CreateUpdateCommand(updateQuery, timeProvider, tableCfg.IdGetter, ok, conn, tx),
            _ => throw new InvalidOperationException($"Invalid completion mode {pollingSettings.CompletionMode}")
        };
        _checkLeftBehindCommandFactory =
            (messages, connection) => CreateCheckLeftBehindCommand(
                SetupCheckLeftBehindQuery(pollingSettings, tableCfg),
                tableCfg.IdGetter,
                messages,
                connection);
    }

    public async Task CompleteAsync(
        IReadOnlyCollection<IMessage> messages,
        MySqlConnection connection,
        MySqlTransaction? tx,
        CancellationToken ct)
    {
        if (messages.Count <= 0) return;

        await using var command = _completeCommandFactory(messages, connection, tx);
        var completed = await command.ExecuteNonQueryAsync(ct);

        // can't think of a reason why this would happen, but checking and throwing just in case
        if (completed != messages.Count) throw new InvalidOperationException("Failed to complete messages");
    }

    async Task IBatchCompleteRetrier.RetryAsync(IReadOnlyCollection<IMessage> messages, CancellationToken ct)
    {
        if (messages.Count <= 0) return;

        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        await using var command = _completeCommandFactory(messages, connection, null);
        var completed = await command.ExecuteNonQueryAsync(ct);

        // other than something unexpected, this could happen if a concurrent process has already completed the messages
        // unlike the original CompleteAsync, where concurrency should be controlled, hence the slight difference in behavior
        if (completed != messages.Count)
        {
            await using var checkLeftBehindCommand = _checkLeftBehindCommandFactory(messages, connection);
            var result = await checkLeftBehindCommand.ExecuteScalarAsync(ct);
            
            var anyMessagesLeftBehind = result switch
            {
                bool b => b,
                int i => i == 1,
                long l => l == 1,
                _ => false
            };

            if (anyMessagesLeftBehind)
            {
                throw new InvalidOperationException("Failed to complete all messages");
            }
        }
    }

    private static MySqlCommand CreateDeleteCommand(
        string deleteQuery,
        Func<IMessage, object> idGetter,
        IReadOnlyCollection<IMessage> ok,
        MySqlConnection connection,
        MySqlTransaction? tx)
    {
        var idParams = string.Join(", ", Enumerable.Range(0, ok.Count).Select(i => $"@id{i}"));
        var command = new MySqlCommand(string.Format(deleteQuery, idParams), connection, tx);

        var i = 0;
        foreach (var m in ok)
        {
            command.Parameters.AddWithValue($"id{i}", idGetter(m));
            i++;
        }

        return command;
    }

    private static MySqlCommand CreateUpdateCommand(
        string updateQuery,
        TimeProvider timeProvider,
        Func<IMessage, object> idGetter,
        IReadOnlyCollection<IMessage> ok,
        MySqlConnection connection,
        MySqlTransaction? tx)
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

        return command;
    }

    private static MySqlCommand CreateCheckLeftBehindCommand(
        string checkPendingQuery,
        Func<IMessage, object> idGetter,
        IReadOnlyCollection<IMessage> messages,
        MySqlConnection connection)
    {
        var idParams = string.Join(", ", Enumerable.Range(0, messages.Count).Select(i => $"@id{i}"));
        var command = new MySqlCommand(string.Format(checkPendingQuery, idParams), connection);

        var i = 0;
        foreach (var m in messages)
        {
            command.Parameters.AddWithValue($"id{i}", idGetter(m));
            i++;
        }

        return command;
    }

    private static string SetupCheckLeftBehindQuery(MySqlPollingSettings pollingSettings, TableConfiguration tableCfg)
        => pollingSettings.CompletionMode == CompletionMode.Delete
            ? $"SELECT EXISTS (SELECT 1 FROM {tableCfg.Name} WHERE {tableCfg.IdColumn} IN ({{0}}));"
            : $"SELECT EXISTS (SELECT 1 FROM {tableCfg.Name} WHERE {tableCfg.IdColumn} IN ({{0}}) AND {tableCfg.ProcessedAtColumn} IS NULL);";

    private static string SetupUpdateQuery(TableConfiguration tableCfg) =>
        $"UPDATE {tableCfg.Name} SET {tableCfg.ProcessedAtColumn} = @processedAt WHERE {tableCfg.IdColumn} IN ({{0}}) AND {tableCfg.ProcessedAtColumn} IS NULL;";

    private static string SetupDeleteQuery(TableConfiguration tableCfg) =>
        $"DELETE FROM {tableCfg.Name} WHERE {tableCfg.IdColumn} IN ({{0}});";
}