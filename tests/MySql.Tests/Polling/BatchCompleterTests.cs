using Dapper;
using MySqlConnector;
using YakShaveFx.OutboxKit.Core.Polling;
using YakShaveFx.OutboxKit.MySql.Polling;
using static YakShaveFx.OutboxKit.MySql.Tests.Polling.TestHelpers;

namespace YakShaveFx.OutboxKit.MySql.Tests.Polling;

public class BatchCompleterTests(MySqlFixture mySqlFixture)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(CompletionMode.Delete)]
    [InlineData(CompletionMode.Update)]
    public async Task WhenCompletingMessagesThenTheyShouldBeCompleted(CompletionMode completionMode)
    {
        var (schemaSettings, mySqlSettings, tableConfig) = GetConfigs(completionMode);
        await using var dbCtx = await mySqlFixture.DbInit
            .WithDefaultSchema(schemaSettings)
            .WithSeed()
            .InitAsync();
        await using var connection = await dbCtx.DataSource.OpenConnectionAsync(_ct);

        var sut = new BatchCompleter(mySqlSettings, tableConfig, dbCtx.DataSource, TimeProvider.System);
        var messages = await GetMessagesAsync(connection, _ct);
        (await AreMessagesCompletedAsync(messages, connection, completionMode)).Should().BeFalse();
        await sut.CompleteAsync(messages, connection, null, _ct);
        (await AreMessagesCompletedAsync(messages, connection, completionMode)).Should().BeTrue();
    }

    [Theory]
    [InlineData(CompletionMode.Delete)]
    [InlineData(CompletionMode.Update)]
    public async Task WhenCompletingAlreadyCompletedMessagesThenAnExceptionIsThrown(CompletionMode completionMode)
    {
        var (schemaSettings, mySqlSettings, tableConfig) = GetConfigs(completionMode);
        await using var dbCtx = await mySqlFixture.DbInit
            .WithDefaultSchema(schemaSettings)
            .WithSeed()
            .InitAsync();
        await using var connection = await dbCtx.DataSource.OpenConnectionAsync(_ct);

        var sut = new BatchCompleter(mySqlSettings, tableConfig, dbCtx.DataSource, TimeProvider.System);

        var messages = await GetMessagesAsync(connection, _ct);
        await CompleteMessagesAsync(messages, connection, completionMode);
        (await AreMessagesCompletedAsync(messages, connection, completionMode)).Should().BeTrue();
        var act = () =>  sut.CompleteAsync(messages, connection, null, _ct);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(CompletionMode.Delete)]
    [InlineData(CompletionMode.Update)]
    public async Task WhenRetryingCompletingMessagesThenTheyShouldBeCompleted(CompletionMode completionMode)
    {
        var (schemaSettings, mySqlSettings, tableConfig) = GetConfigs(completionMode);
        await using var dbCtx = await mySqlFixture.DbInit
            .WithDefaultSchema(schemaSettings)
            .WithSeed()
            .InitAsync();
        await using var connection = await dbCtx.DataSource.OpenConnectionAsync(_ct);

        IBatchCompleteRetrier sut = new BatchCompleter(mySqlSettings, tableConfig, dbCtx.DataSource, TimeProvider.System);
        var messages = await GetMessagesAsync(connection, _ct);
        (await AreMessagesCompletedAsync(messages, connection, completionMode)).Should().BeFalse();
        await sut.RetryAsync(messages, _ct);
        (await AreMessagesCompletedAsync(messages, connection, completionMode)).Should().BeTrue();
    }

    [Theory]
    [InlineData(CompletionMode.Delete)]
    [InlineData(CompletionMode.Update)]
    public async Task WhenRetryingCompletingAlreadyCompletedMessagesThenNoExceptionIsThrown(
        CompletionMode completionMode)
    {
        var (schemaSettings, mySqlSettings, tableConfig) = GetConfigs(completionMode);
        await using var dbCtx = await mySqlFixture.DbInit
            .WithDefaultSchema(schemaSettings)
            .WithSeed()
            .InitAsync();
        await using var connection = await dbCtx.DataSource.OpenConnectionAsync(_ct);

        IBatchCompleteRetrier sut = new BatchCompleter(mySqlSettings, tableConfig, dbCtx.DataSource, TimeProvider.System);

        var messages = await GetMessagesAsync(connection, _ct);
        await CompleteMessagesAsync(messages, connection, completionMode);
        (await AreMessagesCompletedAsync(messages, connection, completionMode)).Should().BeTrue();
        await sut.RetryAsync(messages, _ct);
        (await AreMessagesCompletedAsync(messages, connection, completionMode)).Should().BeTrue();
    }

    private async Task<IReadOnlyCollection<Message>> GetMessagesAsync(
        MySqlConnection connection,
        CancellationToken ct)
        => (await connection.QueryAsync<Message>(
            // lang=mysql
            "SELECT id, type, payload, created_at, trace_context FROM outbox_messages;",
            ct)).ToArray();

    private async Task CompleteMessagesAsync(
        IReadOnlyCollection<Message> messages,
        MySqlConnection connection,
        CompletionMode completionMode)
    {
        if (completionMode == CompletionMode.Delete)
        {
            var result = await connection.ExecuteAsync(
                new CommandDefinition(
                    // lang=mysql
                    "DELETE FROM outbox_messages WHERE id IN @Ids;",
                    new { Ids = messages.Select(m => m.Id) },
                    cancellationToken: _ct));
        }
        else
        {
            var result = await connection.ExecuteAsync(
                new CommandDefinition(
                    // lang=mysql
                    "UPDATE outbox_messages SET processed_at = @CompletedAt WHERE id IN @Ids;",
                    new { CompletedAt = DateTime.UtcNow, Ids = messages.Select(m => m.Id) },
                    cancellationToken: _ct));
        }
    }

    private async Task<bool> AreMessagesCompletedAsync(
        IReadOnlyCollection<Message> messages,
        MySqlConnection connection,
        CompletionMode completionMode)
    {
        if (completionMode == CompletionMode.Delete)
        {
            return await connection.ExecuteScalarAsync<bool>(
                new CommandDefinition(
                    // lang=mysql
                    "SELECT NOT EXISTS(SELECT 1 FROM outbox_messages WHERE id IN @Ids);",
                    new { Ids = messages.Select(m => m.Id) },
                    cancellationToken: _ct));
        }
        else
        {
            return await connection.ExecuteScalarAsync<bool>(
                new CommandDefinition(
                    // lang=mysql
                    "SELECT NOT EXISTS(SELECT 1 FROM outbox_messages WHERE id IN @Ids AND processed_at IS NULL);",
                    new { Ids = messages.Select(m => m.Id) },
                    cancellationToken: _ct));
        }
    }
}