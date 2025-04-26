using System.Linq.Expressions;
using MongoDB.Driver;
using YakShaveFx.OutboxKit.Core;
using YakShaveFx.OutboxKit.Core.CleanUp;

namespace YakShaveFx.OutboxKit.MongoDb.CleanUp;

internal sealed class Cleaner<TMessage>(
    MongoDbCleanUpSettings settings,
    MongoDbCleanUpCollectionSettings<TMessage> collectionSettings,
    IMongoDatabase db,
    TimeProvider timeProvider) : IOutboxCleaner where TMessage : IMessage
{
    private readonly IMongoCollection<TMessage> _collection = db.GetCollection<TMessage>(collectionSettings.Name);

    private readonly Expression<Func<TMessage, DateTime?>> _processedAtSelector =
        collectionSettings.ProcessedAtSelector;

    public async Task<int> CleanAsync(CancellationToken ct)
    {
        var result = await _collection.DeleteManyAsync(
            Builders<TMessage>.Filter.Lt(_processedAtSelector, timeProvider.GetUtcNow().DateTime - settings.MaxAge),
            ct);

        return (int)result.DeletedCount;
    }
}