using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using MongoDB.Driver;
using YakShaveFx.OutboxKit.Core.Polling;
using YakShaveFx.OutboxKit.MongoDb.Polling;
using YakShaveFx.OutboxKit.MongoDb.Synchronization;

namespace YakShaveFx.OutboxKit.MongoDb.Tests.Polling;

public enum CompletionMode
{
    Delete,
    Update
}

public class BatchFetcherTests
{
    private readonly string _databaseName = $"test_{Guid.NewGuid():N}";
    private readonly IMongoDatabase _db;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public BatchFetcherTests(MongoDbFixture fixture) 
        => _db = new MongoClient(fixture.ConnectionString).GetDatabase(_databaseName);

    /*
     * WhenTheOutboxIsPolledConcurrentlyThenTheSecondGetsBlocked and WhenTheOutboxIsPolledConcurrentlyTheSecondIsUnblockedByTheFirstCompleting
     * tests from MySQL don't apply well here, because when we try acquire the lock a second time, it will immediately return an empty batch
     */

    [Theory]
    [InlineData(CompletionMode.Delete)]
    [InlineData(CompletionMode.Update)]
    public async Task WhenTheOutboxIsPolledConcurrentlyThenTheSecondReturnsEmptyEvenIfThereAreMessages(
        CompletionMode completionMode)
    {
        var batchSize = completionMode == CompletionMode.Delete
            ? Defaults.Delete.MongoDbPollingSettings.BatchSize
            : Defaults.Update.MongoDbPollingSettings.BatchSize;

        await SeedAsync(completionMode, batchSize + 1);
        var sut1 = CreateSut(completionMode, TimeProvider.System);
        var sut2 = CreateSut(completionMode, TimeProvider.System);

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
        batch1.Messages.Count.Should().Be(batchSize);

        // the second batch should be empty
        var batch2 = await batch2Task;
        batch2.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenABatchIsProducedThenTheDocumentsAreDeletedFromTheOutbox()
    {
        await SeedAsync(CompletionMode.Delete);
        var sut = CreateSut(CompletionMode.Delete, TimeProvider.System);

        var messagesBefore = await FetchMessageIdsAsync();

        var batchTask = sut.FetchAndHoldAsync(CancellationToken.None);
        await using var batch = await batchTask;
        await batch.CompleteAsync(batch.Messages, CancellationToken.None);

        var messagesAfter = await FetchMessageIdsAsync();

        messagesBefore.Should().Contain(batch.Messages.Select(m => ((TestMessage)m).Id));
        messagesAfter.Count.Should().Be(messagesBefore.Count - Defaults.Delete.MongoDbPollingSettings.BatchSize);
        messagesAfter.Should().NotContain(batch.Messages.Select(m => ((TestMessage)m).Id));
    }

    [Fact]
    public async Task WhenABatchIsProducedThenTheDocumentsAreUpdatedTheOutbox()
    {
        var fakeTimeProvider = new FakeTimeProvider();
        var now = new DateTimeOffset(2024, 11, 11, 20, 33, 45, TimeSpan.Zero);
        fakeTimeProvider.SetUtcNow(now);

        await SeedAsync(CompletionMode.Update);
        var sut = CreateSut(CompletionMode.Update, fakeTimeProvider);

        var messagesBefore = await FetchMessageSummariesAsync();

        var batchTask = sut.FetchAndHoldAsync(CancellationToken.None);
        await using var batch = await batchTask;
        await batch.CompleteAsync(batch.Messages, CancellationToken.None);

        var messagesAfter = await FetchMessageSummariesAsync();

        messagesAfter.Count.Should().Be(messagesBefore.Count);
        messagesAfter.Should().Contain(batch.Messages.Select(m => (((TestMessage)m).Id, (DateTime?)now.DateTime)));
    }

    [Theory]
    [InlineData(CompletionMode.Delete)]
    [InlineData(CompletionMode.Update)]
    public async Task WhenABatchIsProducedButMessagesRemainThenHasNextShouldReturnTrue(CompletionMode completionMode)
    {
        await SeedAsync(completionMode);
        var sut = CreateSut(completionMode, TimeProvider.System);

        var batchTask = sut.FetchAndHoldAsync(CancellationToken.None);
        await using var batch = await batchTask;
        await batch.CompleteAsync(batch.Messages, CancellationToken.None);

        (await batch.HasNextAsync(CancellationToken.None)).Should().BeTrue();
    }

    [Theory]
    [InlineData(CompletionMode.Delete)]
    [InlineData(CompletionMode.Update)]
    public async Task WhenABatchProducesAllRemainingMessagesThenHasNextShouldReturnFalse(CompletionMode completionMode)
    {
        await SeedAsync(completionMode,
            completionMode == CompletionMode.Delete
                ? Defaults.Delete.MongoDbPollingSettings.BatchSize
                : Defaults.Update.MongoDbPollingSettings.BatchSize);
        var sut = CreateSut(completionMode, TimeProvider.System);

        var batch = await sut.FetchAndHoldAsync(CancellationToken.None);
        await batch.CompleteAsync(batch.Messages, CancellationToken.None);

        (await batch.HasNextAsync(CancellationToken.None)).Should().BeFalse();
    }

    private IBatchFetcher CreateSut(
        CompletionMode completionMode,
        TimeProvider timeProvider)
        => completionMode switch
        {
            CompletionMode.Delete => new OutboxBatchFetcher<TestMessage, ObjectId>(
                MongoDbPollingProvider.CreateKey("test"),
                Defaults.Delete.MongoDbPollingSettings,
                Defaults.Delete.CollectionConfig,
                new MongoDbPollingDistributedLockSettings
                {
                    Owner = Guid.NewGuid().ToString()
                },
                _db,
                new DistributedLockThingy(
                    new DistributedLockSettings
                    {
                        ChangeStreamsEnabled = false
                    },
                    _db,
                    timeProvider,
                    NullLogger<DistributedLockThingy>.Instance),
                timeProvider),
            CompletionMode.Update => new OutboxBatchFetcher<TestMessageWithProcessedAt, ObjectId>(
                MongoDbPollingProvider.CreateKey("test"),
                Defaults.Update.MongoDbPollingSettings,
                Defaults.Update.CollectionConfigWithProcessedAt,
                new MongoDbPollingDistributedLockSettings
                {
                    Owner = Guid.NewGuid().ToString()
                },
                _db,
                new DistributedLockThingy(
                    new DistributedLockSettings
                    {
                        ChangeStreamsEnabled = false
                    },
                    _db,
                    timeProvider,
                    NullLogger<DistributedLockThingy>.Instance),
                timeProvider),
            _ => throw new ArgumentOutOfRangeException(nameof(completionMode))
        };

    private async Task<IReadOnlyCollection<ObjectId>> FetchMessageIdsAsync()
    {
        var collection = _db.GetCollection<Message>(Defaults.Delete.CollectionConfig.Name);
        return await collection
            .Find(m => true)
            .Project(m => m.Id)
            .ToListAsync(cancellationToken: _ct);
    }


    private async Task<IReadOnlyCollection<(ObjectId Id, DateTime? ProcessedAt)>> FetchMessageSummariesAsync()
        => (await _db.GetCollection<TestMessageWithProcessedAt>(Defaults.Update.CollectionConfigWithProcessedAt.Name)
                .Find(m => true)
                .Project(m => new { m.Id, m.ProcessedAt })
                .ToListAsync(cancellationToken: _ct))
            .Select(m => (m.Id, m.ProcessedAt))
            .ToArray();

    private async Task SeedAsync(CompletionMode completionMode, int seedCount = 10)
    {
        if (completionMode == CompletionMode.Delete)
        {
            await _db.GetCollection<TestMessage>(Defaults.Delete.CollectionConfig.Name)
                .InsertManyAsync(Enumerable.Range(1, seedCount).Select(i => new TestMessage
                {
                    Id = ObjectId.GenerateNewId(i),
                    Type = "test",
                    Payload = JsonSerializer.SerializeToUtf8Bytes(new { i }),
                    CreatedAt = DateTime.UtcNow,
                    TraceContext = null
                }), cancellationToken: _ct);
        }
        else
        {
            await _db.GetCollection<TestMessageWithProcessedAt>(Defaults.Update.CollectionConfigWithProcessedAt.Name)
                .InsertManyAsync(Enumerable.Range(1, seedCount).Select(i => new TestMessageWithProcessedAt
                {
                    Id = ObjectId.GenerateNewId(i),
                    Type = "test",
                    Payload = JsonSerializer.SerializeToUtf8Bytes(new { i }),
                    CreatedAt = DateTime.UtcNow,
                    ProcessedAt = null,
                    TraceContext = null
                }), cancellationToken: _ct);
        }
    }
}