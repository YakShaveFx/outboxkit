using MongoDB.Bson;
using MongoDB.Driver;
using YakShaveFx.OutboxKit.Core.Polling;
using YakShaveFx.OutboxKit.MongoDb.Polling;

namespace YakShaveFx.OutboxKit.MongoDb.Tests;

internal static class Defaults
{
    internal static class Delete
    {
        internal static readonly CorePollingSettings CorePollingSettings = new()
        {
            EnableCleanUp = false
        };

        internal static readonly MongoDbPollingSettings MongoDbPollingSettings = new()
        {
            BatchSize = 5
        };

        internal static readonly MongoDbPollingCollectionSettings<TestMessage, ObjectId> CollectionConfig = new()
            {
                IdSelector = m => m.Id,
                Sort = Builders<TestMessage>.Sort.Ascending(m => m.Id),
                ProcessedAtSelector = null
            };
    }

    internal static class Update
    {
        internal static readonly CorePollingSettings CorePollingSettings = Delete.CorePollingSettings with
        {
            EnableCleanUp = true
        };

        internal static readonly MongoDbPollingSettings MongoDbPollingSettings = Delete.MongoDbPollingSettings with
        {
            CompletionMode = CompletionMode.Update
        };

        internal static readonly MongoDbPollingCollectionSettings<TestMessageWithProcessedAt, ObjectId>
            CollectionConfigWithProcessedAt = new()
            {
                IdSelector = m => m.Id,
                Sort = Builders<TestMessageWithProcessedAt>.Sort.Ascending(m => m.Id),
                ProcessedAtSelector = m => m.ProcessedAt,
            };
    }
}