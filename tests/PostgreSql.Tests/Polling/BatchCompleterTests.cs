using Dapper;
using Npgsql;
using YakShaveFx.OutboxKit.Core.Polling;
using YakShaveFx.OutboxKit.PostgreSql.Polling;
using static YakShaveFx.OutboxKit.PostgreSql.Tests.Polling.TestHelpers;

namespace YakShaveFx.OutboxKit.PostgreSql.Tests.Polling;

public class BatchCompleterTests(PostgreSqlFixture postgresFixture)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(CompletionMode.Delete)]
    [InlineData(CompletionMode.Update)]
    public async Task WhenCompletingMessagesThenTheyShouldBeCompleted(CompletionMode completionMode)
    {
        var (schemaSettings, postgresSettings, tableConfig) = GetConfigs(completionMode);
        await using var dbCtx = await postgresFixture.DbInit
            .WithDefaultSchema(schemaSettings)
            .WithSeed()
            .InitAsync();
        await using var connection = await dbCtx.DataSource.OpenConnectionAsync(_ct);

        var sut = new BatchCompleter(postgresSettings, tableConfig, dbCtx.DataSource, TimeProvider.System);
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
        var (schemaSettings, postgresSettings, tableConfig) = GetConfigs(completionMode);
        await using var dbCtx = await postgresFixture.DbInit
            .WithDefaultSchema(schemaSettings)
            .WithSeed()
            .InitAsync();
        await using var connection = await dbCtx.DataSource.OpenConnectionAsync(_ct);

        var sut = new BatchCompleter(postgresSettings, tableConfig, dbCtx.DataSource, TimeProvider.System);

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
        var (schemaSettings, postgresSettings, tableConfig) = GetConfigs(completionMode);
        await using var dbCtx = await postgresFixture.DbInit
            .WithDefaultSchema(schemaSettings)
            .WithSeed()
            .InitAsync();
        await using var connection = await dbCtx.DataSource.OpenConnectionAsync(_ct);

        ICompleteRetrier sut = new BatchCompleter(postgresSettings, tableConfig, dbCtx.DataSource, TimeProvider.System);
        var messages = await GetMessagesAsync(connection, _ct);
        (await AreMessagesCompletedAsync(messages, connection, completionMode)).Should().BeFalse();
        await sut.RetryCompleteAsync(messages, _ct);
        (await AreMessagesCompletedAsync(messages, connection, completionMode)).Should().BeTrue();
    }

    [Theory]
    [InlineData(CompletionMode.Delete)]
    [InlineData(CompletionMode.Update)]
    public async Task WhenRetryingCompletingAlreadyCompletedMessagesThenNoExceptionIsThrown(
        CompletionMode completionMode)
    {
        var (schemaSettings, postgresSettings, tableConfig) = GetConfigs(completionMode);
        await using var dbCtx = await postgresFixture.DbInit
            .WithDefaultSchema(schemaSettings)
            .WithSeed()
            .InitAsync();
        await using var connection = await dbCtx.DataSource.OpenConnectionAsync(_ct);

        ICompleteRetrier sut = new BatchCompleter(postgresSettings, tableConfig, dbCtx.DataSource, TimeProvider.System);

        var messages = await GetMessagesAsync(connection, _ct);
        await CompleteMessagesAsync(messages, connection, completionMode);
        (await AreMessagesCompletedAsync(messages, connection, completionMode)).Should().BeTrue();
        await sut.RetryCompleteAsync(messages, _ct);
        (await AreMessagesCompletedAsync(messages, connection, completionMode)).Should().BeTrue();
    }

    private async Task<IReadOnlyCollection<Message>> GetMessagesAsync(
        NpgsqlConnection connection,
        CancellationToken ct)
        => (await connection.QueryAsync<Message>(
            // lang=mysql
            """SELECT "Id", "Type", "Payload", "CreatedAt", "TraceContext" FROM "OutboxMessages";""",
            ct)).ToArray();

    private async Task CompleteMessagesAsync(
        IReadOnlyCollection<Message> messages,
        NpgsqlConnection connection,
        CompletionMode completionMode)
    {
        if (completionMode == CompletionMode.Delete)
        {
            var result = await connection.ExecuteAsync(
                new CommandDefinition(
                    // lang=mysql
                    """DELETE FROM "OutboxMessages" WHERE "Id" = ANY (@Ids);""",
                    new { Ids = messages.Select(m => m.Id).ToArray() },
                    cancellationToken: _ct));
        }
        else
        {
            var result = await connection.ExecuteAsync(
                new CommandDefinition(
                    // lang=mysql
                    """UPDATE "OutboxMessages" SET "ProcessedAt" = @CompletedAt WHERE "Id" = ANY (@Ids);""",
                    new { CompletedAt = DateTime.UtcNow, Ids = messages.Select(m => m.Id).ToArray() },
                    cancellationToken: _ct));
        }
    }

    private async Task<bool> AreMessagesCompletedAsync(
        IReadOnlyCollection<Message> messages,
        NpgsqlConnection connection,
        CompletionMode completionMode)
    {
        if (completionMode == CompletionMode.Delete)
        {
            return await connection.ExecuteScalarAsync<bool>(
                new CommandDefinition(
                    // lang=mysql
                    """SELECT NOT EXISTS(SELECT 1 FROM "OutboxMessages" WHERE "Id" = ANY (@Ids));""",
                    new { Ids = messages.Select(m => m.Id).ToArray() },
                    cancellationToken: _ct));
        }
        else
        {
            return await connection.ExecuteScalarAsync<bool>(
                new CommandDefinition(
                    // lang=mysql
                    """SELECT NOT EXISTS(SELECT 1 FROM "OutboxMessages" WHERE "Id" = ANY (@Ids) AND "ProcessedAt" IS NULL);""",
                    new { Ids = messages.Select(m => m.Id).ToArray() },
                    cancellationToken: _ct));
        }
    }
}