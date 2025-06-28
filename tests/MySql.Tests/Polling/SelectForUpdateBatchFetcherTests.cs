using YakShaveFx.OutboxKit.MySql.Polling;

namespace YakShaveFx.OutboxKit.MySql.Tests.Polling;

public class SelectForUpdateBatchFetcherTests(MySqlFixture mySqlFixture)
{
    private readonly BaseBatchFetcherTests _baseTests = new(
        mySqlFixture,
        (pollingSettings, tableCfg, dataSource, timeProvider) =>
            new SelectForUpdateBatchFetcher(
                pollingSettings,
                tableCfg,
                dataSource,
                new BatchCompleter(pollingSettings, tableCfg, dataSource, timeProvider)));

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
}