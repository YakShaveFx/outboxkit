using System.Linq.Expressions;
using MongoDB.Driver;
using YakShaveFx.OutboxKit.Core;
using YakShaveFx.OutboxKit.Core.Polling;

namespace YakShaveFx.OutboxKit.MongoDb.Polling;

internal sealed class BatchCompleter<TMessage, TId> : IBatchCompleteRetrier where TMessage : IMessage
{
    private readonly IMongoCollection<TMessage> _collection;
    private readonly TimeProvider _timeProvider;
    
    private readonly Expression<Func<TMessage, TId>> _idSelector;
    private readonly Func<IMessage, TId> _idGetter;
    private readonly Expression<Func<TMessage, DateTime?>>? _processedAtSelector;
    private readonly Func<IReadOnlyCollection<IMessage>, CancellationToken, Task> _complete;

    public BatchCompleter(
        MongoDbPollingSettings pollingSettings,
        MongoDbPollingCollectionSettings<TMessage, TId> collectionSettings,
        IMongoDatabase db,
        TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _idSelector = collectionSettings.IdSelector;
        var compiledIdSelector = collectionSettings.IdSelector.Compile();
        _idGetter = m => compiledIdSelector((TMessage)m);
        _processedAtSelector = (pollingSettings.CompletionMode, collectionSettings.ProcessedAtSelector) switch
        {
            (CompletionMode.Delete, _) => null,
            (CompletionMode.Update, not null) => collectionSettings.ProcessedAtSelector,
            _ => throw new ArgumentException("processed at selector not configured", nameof(collectionSettings))
        };
        _collection = db.GetCollection<TMessage>(collectionSettings.Name);
        _complete = pollingSettings.CompletionMode switch
        {
            CompletionMode.Delete => CompleteDeleteAsync,
            CompletionMode.Update => CompleteUpdateAsync,
            _ => throw new ArgumentException("invalid completion mode", nameof(pollingSettings))
        };
    }

    public Task CompleteAsync(IReadOnlyCollection<IMessage> messages, CancellationToken ct) => _complete(messages, ct);
    
    Task IBatchCompleteRetrier.RetryAsync(IReadOnlyCollection<IMessage> messages, CancellationToken ct)
        => _complete(messages, ct);

    private async Task CompleteDeleteAsync(IReadOnlyCollection<IMessage> messages, CancellationToken ct)
    {
        if (messages.Count <= 0) return;

        var result = await _collection.DeleteManyAsync(
            Builders<TMessage>.Filter.In(_idSelector, messages.Select(_idGetter)),
            new DeleteOptions(),
            ct);

        // other than something unexpected, this could happen if a concurrent process has already completed the messages
        if (result.DeletedCount != messages.Count)
        {
            var leftCount = await _collection.CountDocumentsAsync(
                Builders<TMessage>.Filter.In(_idSelector, messages.Select(_idGetter)),
                new CountOptions(),
                ct);

            if (leftCount > 0)
            {
                throw new InvalidOperationException(
                    "Failed to delete all messages, some messages are still present in the collection");
            }
        }
    }

    private async Task CompleteUpdateAsync(IReadOnlyCollection<IMessage> messages, CancellationToken ct)
    {
        if (messages.Count <= 0) return;

        var now = _timeProvider.GetUtcNow().DateTime;
        var result = await _collection.UpdateManyAsync(
            Builders<TMessage>.Filter.In(_idSelector, messages.Select(_idGetter)),
            Builders<TMessage>.Update.Set(_processedAtSelector, now),
            new UpdateOptions(),
            ct);

        // other than something unexpected, this could happen if a concurrent process has already completed the messages
        if (result.ModifiedCount != messages.Count)
        {
            var leftCount = await _collection.CountDocumentsAsync(
                Builders<TMessage>.Filter.And(
                    Builders<TMessage>.Filter.In(_idSelector, messages.Select(_idGetter)),
                    Builders<TMessage>.Filter.Eq(_processedAtSelector, null)),
                new CountOptions(),
                ct);

            if (leftCount > 0)
            {
                throw new InvalidOperationException(
                    "Failed to update all messages, some messages are still not marked as processed");
            }
        }
    }
}