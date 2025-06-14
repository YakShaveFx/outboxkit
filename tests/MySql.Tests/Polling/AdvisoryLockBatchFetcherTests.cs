using Dapper;
using YakShaveFx.OutboxKit.MySql.Polling;
using static YakShaveFx.OutboxKit.MySql.Tests.Polling.BatchFetcherTestHelpers;

namespace YakShaveFx.OutboxKit.MySql.Tests.Polling;

[Collection(MySqlCollection.Name)]
public class AdvisoryLockBatchFetcherTests(MySqlFixture mySqlFixture)
{
    private readonly BaseBatchFetcherTests _baseTests = new(
        mySqlFixture,
        (pollingSettings, tableCfg, dataSource, timeProvider) =>
            new AdvisoryLockBatchFetcher(pollingSettings, tableCfg, dataSource, timeProvider));

    [Theory]
    [InlineData(CompletionMode.Delete)]
    [InlineData(CompletionMode.Update)]
    public Task WhenTheOutboxIsPolledConcurrentlyThenTheSecondGetsBlocked(CompletionMode completionMode)
        => _baseTests.WhenTheOutboxIsPolledConcurrentlyThenTheSecondGetsBlocked(completionMode);

    [Theory]
    [InlineData(CompletionMode.Delete)]
    [InlineData(CompletionMode.Update)]
    public Task WhenTheOutboxIsPolledConcurrentlyTheSecondIsUnblockedByTheFirstCompleting(CompletionMode completionMode)
        => _baseTests.WhenTheOutboxIsPolledConcurrentlyTheSecondIsUnblockedByTheFirstCompleting(completionMode);

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
        var (schemaSettings, mySqlSettings, tableConfig) = GetConfigs(completionMode);
        await using var dbCtx = await mySqlFixture.DbInit
            .WithDefaultSchema(schemaSettings)
            .WithSeed(seed ? 10 : 0)
            .InitAsync();
        await using var connection = await dbCtx.DataSource.OpenConnectionAsync();

        var sut = new AdvisoryLockBatchFetcher(mySqlSettings, tableConfig, dbCtx.DataSource, TimeProvider.System);

        await using var _ = await sut.FetchAndHoldAsync(CancellationToken.None);

        // lang=mysql
        var hasLock = await connection.ExecuteScalarAsync<bool>(
            $"SELECT IS_USED_LOCK('outboxkit_{dbCtx.DatabaseName}');");
        
        hasLock.Should().Be(shouldHaveLock);
    }
}