using Dapper;
using FluentAssertions;
using MySqlConnector;
using YakShaveFx.OutboxKit.MySql.Polling;
using static YakShaveFx.OutboxKit.MySql.Tests.Polling.Defaults;

namespace YakShaveFx.OutboxKit.MySql.Tests.Polling;

[Collection(MySqlCollection.Name)]
public class OutboxBatchFetcherTests(MySqlFixture mySqlFixture)
{
    [Fact]
    public async Task WhenTheOutboxIsPolledConcurrentlyThenTheSecondGetsBlocked()
    {
        await using var databaseContext = await mySqlFixture.DbInitializer.WithDefaultSchema().WithSeed().InitAsync();
        await using var connection = await databaseContext.DataSource.OpenConnectionAsync();

        var sut1 = new OutboxBatchFetcher(DefaultPollingSettings, DefaultTableConfig, databaseContext.DataSource);
        var sut2 = new OutboxBatchFetcher(DefaultPollingSettings, DefaultTableConfig, databaseContext.DataSource);

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
            IsCompletedSuccessfully = true,
        });
        batch2Task.Should().BeEquivalentTo(new
        {
            IsCompleted = false,
            IsCompletedSuccessfully = false,
        });
    }

    [Fact]
    public async Task WhenTheOutboxIsPolledConcurrentlyTheSecondIsUnblockedByTheFirstCompleting()
    {
        await using var databaseContext = await mySqlFixture.DbInitializer.WithDefaultSchema().WithSeed().InitAsync();
        await using var connection = await databaseContext.DataSource.OpenConnectionAsync();

        var sut1 = new OutboxBatchFetcher(DefaultPollingSettings, DefaultTableConfig, databaseContext.DataSource);
        var sut2 = new OutboxBatchFetcher(DefaultPollingSettings, DefaultTableConfig, databaseContext.DataSource);

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

    [Fact]
    public async Task WhenABatchIsProducedThenTheRowsAreDeletedFromTheOutbox()
    {
        await using var databaseContext = await mySqlFixture.DbInitializer.WithDefaultSchema().WithSeed().InitAsync();
        await using var connection = await databaseContext.DataSource.OpenConnectionAsync();

        var sut = new OutboxBatchFetcher(DefaultPollingSettings, DefaultTableConfig, databaseContext.DataSource);

        var messagesBefore = await FetchMessageIdsAsync(connection);

        var batchTask = sut.FetchAndHoldAsync(CancellationToken.None);
        await using var batch = await batchTask;
        await batch.CompleteAsync(batch.Messages, CancellationToken.None);

        var messagesAfter = await FetchMessageIdsAsync(connection);

        messagesBefore.Should().Contain(batch.Messages.Select(m => ((Message)m).Id));
        messagesAfter.Count.Should().Be(messagesBefore.Count - DefaultPollingSettings.BatchSize);
        messagesAfter.Should().NotContain(batch.Messages.Select(m => ((Message)m).Id));
    }

    [Fact]
    public async Task WhenABatchIsProducedButMessagesRemainThenHasNextShouldReturnTrue()
    {
        await using var databaseContext = await mySqlFixture.DbInitializer.WithDefaultSchema().WithSeed().InitAsync();
        await using var connection = await databaseContext.DataSource.OpenConnectionAsync();

        var sut = new OutboxBatchFetcher(DefaultPollingSettings, DefaultTableConfig, databaseContext.DataSource);

        var batchTask = sut.FetchAndHoldAsync(CancellationToken.None);
        await using var batch = await batchTask;
        await batch.CompleteAsync(batch.Messages, CancellationToken.None);

        (await batch.HasNextAsync(CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task WhenABatchProducesAllRemainingMessagesThenHasNextShouldReturnFalse()
    {
        await using var databaseContext = await mySqlFixture.DbInitializer
            .WithDefaultSchema()
            .WithSeed(seedCount: DefaultPollingSettings.BatchSize)
            .InitAsync();
        await using var connection = await databaseContext.DataSource.OpenConnectionAsync();

        var sut = new OutboxBatchFetcher(DefaultPollingSettings, DefaultTableConfig, databaseContext.DataSource);

        var batchTask = sut.FetchAndHoldAsync(CancellationToken.None);
        await using var batch = await batchTask;
        await batch.CompleteAsync(batch.Messages, CancellationToken.None);

        (await batch.HasNextAsync(CancellationToken.None)).Should().BeFalse();
    }

    private static async Task<IReadOnlyCollection<long>> FetchMessageIdsAsync(MySqlConnection connection)
        => (await connection.QueryAsync<long>("SELECT id FROM outbox_messages;")).ToArray();
}