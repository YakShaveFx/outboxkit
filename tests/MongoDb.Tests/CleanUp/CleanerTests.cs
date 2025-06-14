using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Driver;
using YakShaveFx.OutboxKit.MongoDb.CleanUp;

namespace YakShaveFx.OutboxKit.MongoDb.Tests.CleanUp;

public class CleanerTests
{
    private readonly string _databaseName = $"test_{Guid.NewGuid():N}";
    private readonly MongoDbFixture _fixture;
    private readonly IMongoDatabase _db;

    public CleanerTests(MongoDbFixture fixture)
    {
        _fixture = fixture;
        _db = new MongoClient(fixture.ConnectionString).GetDatabase(_databaseName);
    }
    
    [Theory]
    [InlineData(10, 0, 0)]
    [InlineData(10, 5, 5)]
    [InlineData(10, 10, 0)]
    [InlineData(10, 0, 5)]
    [InlineData(10, 0, 10)]
    public async Task WhenCleaningUpTheOutboxAllExpiredProcessedMessagesAreDeletedWhileRecentOrUnprocessedRemain(
        int totalCount,
        int processedExpiredCount,
        int processedRecentCount)
    {
        var collectionConfig = Defaults.Update.CollectionConfigWithProcessedAt;
        var cleanupSettings = new MongoDbCleanUpSettings();
        var now = new DateTimeOffset(2024, 11, 13, 18, 36, 45, TimeSpan.Zero);
        var fakeTimeProvider = new FakeTimeProvider();
        fakeTimeProvider.SetUtcNow(now);
        var collection = _db.GetCollection<TestMessageWithProcessedAt>(collectionConfig.Name);

        var messages = Enumerable.Range(0, totalCount)
            .Select(i => new TestMessageWithProcessedAt
            {
                Type = "test",
                Payload = JsonSerializer.SerializeToUtf8Bytes(new { i }),
                CreatedAt = (now - cleanupSettings.MaxAge).AddDays(-1).DateTime,
                ProcessedAt = i switch
                {
                    _ when i < processedExpiredCount => now.Add(-cleanupSettings.MaxAge).AddMinutes(-1).DateTime,
                    _ when i >= processedExpiredCount && i < processedExpiredCount + processedRecentCount => now
                        .Add(-cleanupSettings.MaxAge).AddMinutes(1).DateTime,
                    _ => null
                },
                TraceContext = null
            })
            .ToArray();
        await collection.InsertManyAsync(messages);

        var sut = new Cleaner<TestMessageWithProcessedAt>(
            cleanupSettings,
            new MongoDbCleanUpCollectionSettings<TestMessageWithProcessedAt>
            {
                Name = collectionConfig.Name,
                ProcessedAtSelector = m => m.ProcessedAt
            },
            _db,
            fakeTimeProvider);

        var deletedCount = await sut.CleanAsync(CancellationToken.None);
        deletedCount.Should().Be(processedExpiredCount);
    }
}