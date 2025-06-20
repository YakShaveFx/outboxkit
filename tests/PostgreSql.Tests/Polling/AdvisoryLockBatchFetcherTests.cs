using Dapper;
using Npgsql;
using YakShaveFx.OutboxKit.Core.Polling;
using YakShaveFx.OutboxKit.PostgreSql.Polling;
using YakShaveFx.OutboxKit.PostgreSql.Shared;
using static YakShaveFx.OutboxKit.PostgreSql.Tests.Polling.TestHelpers;

namespace YakShaveFx.OutboxKit.PostgreSql.Tests.Polling;

public class AdvisoryLockBatchFetcherTests(PostgreSqlFixture postgresFixture)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private readonly BaseBatchFetcherTests _baseTests = new(
        postgresFixture,
        SutFactory);

    /*
     * WhenTheOutboxIsPolledConcurrentlyThenTheSecondGetsBlocked and WhenTheOutboxIsPolledConcurrentlyTheSecondIsUnblockedByTheFirstCompleting
     * tests from SELECT...FOR UPDATE (and MySQL) don't apply well here, because when we try to acquire the lock a second time, it will immediately return an empty batch
     */

    [Theory]
    [InlineData(CompletionMode.Delete)]
    [InlineData(CompletionMode.Update)]
    public async Task WhenTheOutboxIsPolledConcurrentlyThenTheSecondReturnsEmptyEvenIfThereAreMessages(
        CompletionMode completionMode)
    {
        var (schemaSettings, postgresSettings, tableConfig) = GetConfigs(completionMode);
        await using var dbCtx = await postgresFixture.DbInit.WithDefaultSchema(schemaSettings).WithSeed().InitAsync();
        await using var connection = await dbCtx.DataSource.OpenConnectionAsync(_ct);

        var sut1 = SutFactory(postgresSettings, tableConfig, dbCtx.DataSource, TimeProvider.System);
        var sut2 = SutFactory(postgresSettings, tableConfig, dbCtx.DataSource, TimeProvider.System);

        // start fetching from the outbox concurrently
        // - first delay is to ensure the first query is executed before the second one
        // - second delay is to give the second query time to return an empty batch
        // (if there's a better way to test this, I'm all ears 😅)
        var batch1Task = sut1.FetchAndHoldAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(1), _ct);
        var batch2Task = sut2.FetchAndHoldAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(1), _ct);

        // both tasks should be completed
        batch1Task.Should().BeEquivalentTo(new
        {
            IsCompleted = true,
            IsCompletedSuccessfully = true
        });
        batch2Task.Should().BeEquivalentTo(new
        {
            IsCompleted = true,
            IsCompletedSuccessfully = true
        });

        // the first batch should contain messages
        var batch1 = await batch1Task;
        batch1.Messages.Count.Should().Be(postgresSettings.BatchSize);

        // the second batch should be empty
        var batch2 = await batch2Task;
        batch2.Messages.Should().BeEmpty();
    }

    [Fact]
    public Task WhenABatchIsProducedThenTheRowsAreDeletedFromTheOutbox()
        => _baseTests.WhenABatchIsProducedThenTheRowsAreDeletedFromTheOutbox();

    [Fact]
    public Task WhenABatchIsProducedThenTheRowsAreUpdatedTheOutbox()
        => _baseTests.WhenABatchIsProducedThenTheRowsAreUpdatedTheOutbox();

    [Theory]
    [InlineData(CompletionMode.Delete)]
    [InlineData(CompletionMode.Update)]
    public Task WhenABatchIsProducedButMessagesRemainThenHasNextShouldReturnTrue(CompletionMode completionMode)
        => _baseTests.WhenABatchIsProducedButMessagesRemainThenHasNextShouldReturnTrue(completionMode);

    [Theory]
    [InlineData(CompletionMode.Delete)]
    [InlineData(CompletionMode.Update)]
    public Task WhenABatchProducesAllRemainingMessagesThenHasNextShouldReturnFalse(CompletionMode completionMode)
        => _baseTests.WhenABatchProducesAllRemainingMessagesThenHasNextShouldReturnFalse(completionMode);
    
    [Theory]
    [InlineData(CompletionMode.Delete, false, false)]
    [InlineData(CompletionMode.Delete, true, true)]
    [InlineData(CompletionMode.Update, false, false)]
    [InlineData(CompletionMode.Update, true, true)]
    public async Task WhenFetchingABatchLockShouldBeRetainedIfMessagesAreFound(CompletionMode completionMode, bool seed, bool shouldHaveLock)
    {
        var (schemaSettings, postgresSettings, tableConfig) = GetConfigs(completionMode);
        await using var dbCtx = await postgresFixture.DbInit
            .WithDefaultSchema(schemaSettings)
            .WithSeed(seed ? 10 : 0)
            .InitAsync();
        
        var sut = new AdvisoryLockBatchFetcher(postgresSettings, tableConfig, dbCtx.DataSource, new BatchCompleter(postgresSettings, tableConfig, dbCtx.DataSource, TimeProvider.System));

        await using var _ = await sut.FetchAndHoldAsync(CancellationToken.None);
        
        await using var connection = await dbCtx.DataSource.OpenConnectionAsync(_ct);
        // lang=postgresql
        var hasLock = !await connection.ExecuteScalarAsync<bool>(
            "SELECT pg_try_advisory_lock(@key1, @key2);",
            new
            {
                key1 = "outboxkit".GetHashCode(),
                key2 = dbCtx.DatabaseName.GetHashCode()
            });
        
        hasLock.Should().Be(shouldHaveLock);
    }
    
    private static IBatchFetcher SutFactory(PostgreSqlPollingSettings pollingSettings, TableConfiguration tableCfg, NpgsqlDataSource dataSource, TimeProvider timeProvider) 
        => new AdvisoryLockBatchFetcher(pollingSettings, tableCfg, dataSource, new BatchCompleter(pollingSettings, tableCfg, dataSource, timeProvider));
}