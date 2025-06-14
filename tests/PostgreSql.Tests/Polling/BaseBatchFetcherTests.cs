using Dapper;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using YakShaveFx.OutboxKit.Core.Polling;
using YakShaveFx.OutboxKit.PostgreSql.Polling;
using YakShaveFx.OutboxKit.PostgreSql.Shared;
using static YakShaveFx.OutboxKit.PostgreSql.Tests.Polling.BatchFetcherTestHelpers;

namespace YakShaveFx.OutboxKit.PostgreSql.Tests.Polling;

public enum CompletionMode
{
    Delete,
    Update
}

internal delegate IBatchFetcher BatchFetcherFactory(
    PostgreSqlPollingSettings pollingSettings,
    TableConfiguration tableCfg,
    NpgsqlDataSource dataSource,
    TimeProvider timeProvider);

internal class BaseBatchFetcherTests(PostgreSqlFixture postgresFixture, BatchFetcherFactory sutFactory)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    
    public async Task WhenTheOutboxIsPolledConcurrentlyThenTheSecondGetsBlocked(CompletionMode completionMode)
    {
        var (schemaSettings, postgresSettings, tableConfig) = GetConfigs(completionMode);
        await using var dbCtx = await postgresFixture.DbInit.WithDefaultSchema(schemaSettings).WithSeed().InitAsync();
        await using var connection = await dbCtx.DataSource.OpenConnectionAsync(_ct);

        var sut1 = sutFactory(postgresSettings, tableConfig, dbCtx.DataSource, TimeProvider.System);
        var sut2 = sutFactory(postgresSettings, tableConfig, dbCtx.DataSource, TimeProvider.System);

        // start fetching from the outbox concurrently
        // - first delay is to ensure the first query is executed before the second one
        // - second delay is to give the second query time to block
        // (if there's a better way to test this, I'm all ears 😅)
        var batch1Task = sut1.FetchAndHoldAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(1), _ct);
        var batch2Task = sut2.FetchAndHoldAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(1), _ct);

        batch1Task.Should().BeEquivalentTo(new
        {
            IsCompleted = true,
            IsCompletedSuccessfully = true
        });
        batch2Task.Should().BeEquivalentTo(new
        {
            IsCompleted = false,
            IsCompletedSuccessfully = false
        });
    }

    public async Task WhenTheOutboxIsPolledConcurrentlyTheSecondIsUnblockedByTheFirstCompleting(
        CompletionMode completionMode)
    {
        var (schemaSettings, postgresSettings, tableConfig) = GetConfigs(completionMode);
        await using var dbCtx = await postgresFixture.DbInit.WithDefaultSchema(schemaSettings).WithSeed().InitAsync();
        await using var connection = await dbCtx.DataSource.OpenConnectionAsync(_ct);

        var sut1 = sutFactory(postgresSettings, tableConfig, dbCtx.DataSource, TimeProvider.System);
        var sut2 = sutFactory(postgresSettings, tableConfig, dbCtx.DataSource, TimeProvider.System);

        // start fetching from the outbox concurrently
        // - first delay is to ensure the first query is executed before the second one
        // - second delay is to give the second query time to block
        // (if there's a better way to test this, I'm all ears 😅)
        var batch1Task = sut1.FetchAndHoldAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(1), _ct);
        var batch2Task = sut2.FetchAndHoldAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(1), _ct);
        
        batch1Task.Should().BeEquivalentTo(new
        {
            IsCompleted = true,
            IsCompletedSuccessfully = true
        });
        batch2Task.Should().BeEquivalentTo(new
        {
            IsCompleted = false,
            IsCompletedSuccessfully = false
        });
        
        await using var batch1 = await batch1Task;
        await batch1.CompleteAsync(batch1.Messages, CancellationToken.None);
        batch1.Messages.Cast<Message>().Should().AllSatisfy(m => m.Id.Should().BeInRange(1, 5));

        await using var batch2 = await batch2Task;
        await batch2.CompleteAsync(batch2.Messages, CancellationToken.None);
        batch2.Messages.Cast<Message>().Should().AllSatisfy(m => m.Id.Should().BeInRange(6, 10));
    }
    
    public async Task WhenABatchIsProducedThenTheRowsAreDeletedFromTheOutbox()
    {
        var (schemaSettings, postgresSettings, tableConfig) = GetConfigs(CompletionMode.Delete);
        await using var dbCtx = await postgresFixture.DbInit.WithDefaultSchema(schemaSettings).WithSeed().InitAsync();
        await using var connection = await dbCtx.DataSource.OpenConnectionAsync(_ct);

        var sut = sutFactory(postgresSettings, tableConfig, dbCtx.DataSource, TimeProvider.System);

        var messagesBefore = await FetchMessageIdsAsync(connection);

        var batchTask = sut.FetchAndHoldAsync(CancellationToken.None);
        await using var batch = await batchTask;
        await batch.CompleteAsync(batch.Messages, CancellationToken.None);

        var messagesAfter = await FetchMessageIdsAsync(connection);

        messagesBefore.Should().Contain(batch.Messages.Select(m => ((Message)m).Id));
        messagesAfter.Count.Should().Be(messagesBefore.Count - postgresSettings.BatchSize);
        messagesAfter.Should().NotContain(batch.Messages.Select(m => ((Message)m).Id));
    }


    public async Task WhenABatchIsProducedThenTheRowsAreUpdatedTheOutbox()
    {
        var (schemaSettings, postgresSettings, tableConfig) = GetConfigs(CompletionMode.Update);
        var fakeTimeProvider = new FakeTimeProvider();
        var now = new DateTimeOffset(2024, 11, 11, 20, 33, 45, TimeSpan.Zero);
        fakeTimeProvider.SetUtcNow(now);
        await using var dbCtx = await postgresFixture.DbInit.WithDefaultSchema(schemaSettings).WithSeed().InitAsync();

        await using var connection = await dbCtx.DataSource.OpenConnectionAsync(_ct);

        var sut = sutFactory(postgresSettings, tableConfig, dbCtx.DataSource, fakeTimeProvider);

        var messagesBefore = await FetchMessageSummariesAsync(connection);

        var batchTask = sut.FetchAndHoldAsync(CancellationToken.None);
        await using var batch = await batchTask;
        await batch.CompleteAsync(batch.Messages, CancellationToken.None);

        var messagesAfter = await FetchMessageSummariesAsync(connection);

        messagesAfter.Count.Should().Be(messagesBefore.Count);
        messagesAfter.Should().Contain(batch.Messages.Select(m => (((Message)m).Id, (DateTime?)now.DateTime)));
    }

    public async Task WhenABatchIsProducedButMessagesRemainThenHasNextShouldReturnTrue(CompletionMode completionMode)
    {
        var (schemaSettings, postgresSettings, tableConfig) = GetConfigs(completionMode);
        await using var dbCtx = await postgresFixture.DbInit.WithDefaultSchema(schemaSettings).WithSeed().InitAsync();
        await using var connection = await dbCtx.DataSource.OpenConnectionAsync(_ct);

        var sut = sutFactory(postgresSettings, tableConfig, dbCtx.DataSource, TimeProvider.System);

        var batchTask = sut.FetchAndHoldAsync(CancellationToken.None);
        await using var batch = await batchTask;
        await batch.CompleteAsync(batch.Messages, CancellationToken.None);

        (await batch.HasNextAsync(CancellationToken.None)).Should().BeTrue();
    }
    
    public async Task WhenABatchProducesAllRemainingMessagesThenHasNextShouldReturnFalse(CompletionMode completionMode)
    {
        var (schemaSettings, postgresSettings, tableConfig) = GetConfigs(completionMode);
        await using var dbCtx = await postgresFixture.DbInit
            .WithDefaultSchema(schemaSettings)
            .WithSeed(seedCount: postgresSettings.BatchSize)
            .InitAsync();
        await using var connection = await dbCtx.DataSource.OpenConnectionAsync(_ct);

        var sut = sutFactory(postgresSettings, tableConfig, dbCtx.DataSource, TimeProvider.System);

        var batch = await sut.FetchAndHoldAsync(CancellationToken.None);
        await batch.CompleteAsync(batch.Messages, CancellationToken.None);

        (await batch.HasNextAsync(CancellationToken.None)).Should().BeFalse();
    }

    private static async Task<IReadOnlyCollection<long>> FetchMessageIdsAsync(NpgsqlConnection connection)
        => (await connection.QueryAsync<long>("SELECT id FROM outbox_messages;")).ToArray();

    private static async Task<IReadOnlyCollection<(long Id, DateTime? ProcessedAt)>> FetchMessageSummariesAsync(
        NpgsqlConnection connection)
        => (await connection.QueryAsync<(long Id, DateTime? ProcessedAt)>(
            "SELECT id, processed_at FROM outbox_messages;")).ToArray();
}