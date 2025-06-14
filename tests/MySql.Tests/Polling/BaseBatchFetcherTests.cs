using Dapper;
using Microsoft.Extensions.Time.Testing;
using MySqlConnector;
using YakShaveFx.OutboxKit.Core.Polling;
using YakShaveFx.OutboxKit.MySql.Polling;
using YakShaveFx.OutboxKit.MySql.Shared;
using static YakShaveFx.OutboxKit.MySql.Tests.Polling.BatchFetcherTestHelpers;

namespace YakShaveFx.OutboxKit.MySql.Tests.Polling;

public enum CompletionMode
{
    Delete,
    Update
}

internal delegate IBatchFetcher BatchFetcherFactory(
    MySqlPollingSettings pollingSettings,
    TableConfiguration tableCfg,
    MySqlDataSource dataSource,
    TimeProvider timeProvider);

internal class BaseBatchFetcherTests(MySqlFixture mySqlFixture, BatchFetcherFactory sutFactory)
{
    public async Task WhenTheOutboxIsPolledConcurrentlyThenTheSecondGetsBlocked(CompletionMode completionMode)
    {
        var (schemaSettings, mySqlSettings, tableConfig) = GetConfigs(completionMode);
        await using var dbCtx = await mySqlFixture.DbInit.WithDefaultSchema(schemaSettings).WithSeed().InitAsync();
        await using var connection = await dbCtx.DataSource.OpenConnectionAsync();

        var sut1 = sutFactory(mySqlSettings, tableConfig, dbCtx.DataSource, TimeProvider.System);
        var sut2 = sutFactory(mySqlSettings, tableConfig, dbCtx.DataSource, TimeProvider.System);

        // start fetching from the outbox concurrently
        // - first delay is to ensure the first query is executed before the second one
        // - second delay is to give the second query time to block
        // (if there's a better way to test this, I'm all ears 😅)
        var batch1Task = sut1.FetchAndHoldAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(1));
        var batch2Task = sut2.FetchAndHoldAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(1));

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
        var (schemaSettings, mySqlSettings, tableConfig) = GetConfigs(completionMode);
        await using var dbCtx = await mySqlFixture.DbInit.WithDefaultSchema(schemaSettings).WithSeed().InitAsync();
        await using var connection = await dbCtx.DataSource.OpenConnectionAsync();

        var sut1 = sutFactory(mySqlSettings, tableConfig, dbCtx.DataSource, TimeProvider.System);
        var sut2 = sutFactory(mySqlSettings, tableConfig, dbCtx.DataSource, TimeProvider.System);

        var batch1Task = sut1.FetchAndHoldAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(1));
        var batch2Task = sut2.FetchAndHoldAsync(CancellationToken.None);

        await using var batch1 = await batch1Task;
        await batch1.CompleteAsync(batch1.Messages, CancellationToken.None);
        batch1.Messages.Cast<Message>().Should().AllSatisfy(m => m.Id.Should().BeInRange(1, 5));

        await using var batch2 = await batch2Task;
        await batch2.CompleteAsync(batch2.Messages, CancellationToken.None);
        batch2.Messages.Cast<Message>().Should().AllSatisfy(m => m.Id.Should().BeInRange(6, 10));
    }
    
    public async Task WhenABatchIsProducedThenTheRowsAreDeletedFromTheOutbox()
    {
        var (schemaSettings, mySqlSettings, tableConfig) = GetConfigs(CompletionMode.Delete);
        await using var dbCtx = await mySqlFixture.DbInit.WithDefaultSchema(schemaSettings).WithSeed().InitAsync();
        await using var connection = await dbCtx.DataSource.OpenConnectionAsync();

        var sut = sutFactory(mySqlSettings, tableConfig, dbCtx.DataSource, TimeProvider.System);

        var messagesBefore = await FetchMessageIdsAsync(connection);

        var batchTask = sut.FetchAndHoldAsync(CancellationToken.None);
        await using var batch = await batchTask;
        await batch.CompleteAsync(batch.Messages, CancellationToken.None);

        var messagesAfter = await FetchMessageIdsAsync(connection);

        messagesBefore.Should().Contain(batch.Messages.Select(m => ((Message)m).Id));
        messagesAfter.Count.Should().Be(messagesBefore.Count - mySqlSettings.BatchSize);
        messagesAfter.Should().NotContain(batch.Messages.Select(m => ((Message)m).Id));
    }


    public async Task WhenABatchIsProducedThenTheRowsAreUpdatedTheOutbox()
    {
        var (schemaSettings, mySqlSettings, tableConfig) = GetConfigs(CompletionMode.Update);
        var fakeTimeProvider = new FakeTimeProvider();
        var now = new DateTimeOffset(2024, 11, 11, 20, 33, 45, TimeSpan.Zero);
        fakeTimeProvider.SetUtcNow(now);
        await using var dbCtx = await mySqlFixture.DbInit.WithDefaultSchema(schemaSettings).WithSeed().InitAsync();

        await using var connection = await dbCtx.DataSource.OpenConnectionAsync();

        var sut = sutFactory(mySqlSettings, tableConfig, dbCtx.DataSource, fakeTimeProvider);

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
        var (schemaSettings, mySqlSettings, tableConfig) = GetConfigs(completionMode);
        await using var dbCtx = await mySqlFixture.DbInit.WithDefaultSchema(schemaSettings).WithSeed().InitAsync();
        await using var connection = await dbCtx.DataSource.OpenConnectionAsync();

        var sut = sutFactory(mySqlSettings, tableConfig, dbCtx.DataSource, TimeProvider.System);

        var batchTask = sut.FetchAndHoldAsync(CancellationToken.None);
        await using var batch = await batchTask;
        await batch.CompleteAsync(batch.Messages, CancellationToken.None);

        (await batch.HasNextAsync(CancellationToken.None)).Should().BeTrue();
    }
    
    public async Task WhenABatchProducesAllRemainingMessagesThenHasNextShouldReturnFalse(CompletionMode completionMode)
    {
        var (schemaSettings, mySqlSettings, tableConfig) = GetConfigs(completionMode);
        await using var dbCtx = await mySqlFixture.DbInit
            .WithDefaultSchema(schemaSettings)
            .WithSeed(seedCount: mySqlSettings.BatchSize)
            .InitAsync();
        await using var connection = await dbCtx.DataSource.OpenConnectionAsync();

        var sut = sutFactory(mySqlSettings, tableConfig, dbCtx.DataSource, TimeProvider.System);

        var batch = await sut.FetchAndHoldAsync(CancellationToken.None);
        await batch.CompleteAsync(batch.Messages, CancellationToken.None);

        (await batch.HasNextAsync(CancellationToken.None)).Should().BeFalse();
    }

    private static async Task<IReadOnlyCollection<long>> FetchMessageIdsAsync(MySqlConnection connection)
        => (await connection.QueryAsync<long>("SELECT id FROM outbox_messages;")).ToArray();

    private static async Task<IReadOnlyCollection<(long Id, DateTime? ProcessedAt)>> FetchMessageSummariesAsync(
        MySqlConnection connection)
        => (await connection.QueryAsync<(long Id, DateTime? ProcessedAt)>(
            "SELECT id, processed_at FROM outbox_messages;")).ToArray();
}