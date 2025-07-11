using MongoDB.Driver;
using YakShaveFx.OutboxKit.Core;
using YakShaveFx.OutboxKit.Core.Polling;
using YakShaveFx.OutboxKit.MongoDb.Synchronization;

namespace YakShaveFx.OutboxKit.MongoDb.Polling;

// ReSharper disable once ClassNeverInstantiated.Global - automagically instantiated by DI
internal sealed class BatchFetcher<TMessage, TId> : IBatchFetcher where TMessage : IMessage
{
    private readonly IMongoCollection<TMessage> _collection;
    private readonly DistributedLockThingy _lockThingy;

    private readonly int _batchSize;
    private readonly string _lockId;
    private readonly string _lockOwner;
    private readonly string _lockContext;
    private readonly FilterDefinition<TMessage> _findFilter;
    private readonly SortDefinition<TMessage> _sort;
    private readonly Func<CancellationToken, Task<bool>> _hasNext;
    private readonly BatchCompleter<TMessage, TId> _completer;

    public BatchFetcher(
        OutboxKey key,
        MongoDbPollingSettings pollingSettings,
        MongoDbPollingCollectionSettings<TMessage, TId> collectionSettings,
        MongoDbPollingDistributedLockSettings lockSettings,
        IMongoDatabase db,
        DistributedLockThingy lockThingy,
        BatchCompleter<TMessage, TId> completer)
    {
        _lockId = lockSettings.Id;
        _lockOwner = lockSettings.Owner;
        _lockContext = key.ToString();
        _lockThingy = lockThingy;
        _batchSize = pollingSettings.BatchSize;
        _findFilter = (pollingSettings.CompletionMode, collectionSettings.ProcessedAtSelector) switch
        {
            (CompletionMode.Delete, _) => Builders<TMessage>.Filter.Empty,
            (CompletionMode.Update, not null) =>
                Builders<TMessage>.Filter.Eq(collectionSettings.ProcessedAtSelector, null),
            _ => throw new ArgumentException("processed at selector not configured", nameof(collectionSettings))
        };
        _sort = collectionSettings.Sort;
        _collection = db.GetCollection<TMessage>(collectionSettings.Name);
        _completer = completer;
        _hasNext = HasNextAsync;
    }

    public async Task<IBatchContext> FetchAndHoldAsync(CancellationToken ct)
    {
        var @lock = await _lockThingy.TryAcquireAsync(
            new() { Id = _lockId, Owner = _lockOwner, Context = _lockContext },
            ct);

        // if we can't acquire, we can assume that another instance is already processing the outbox, so we can just return
        if (@lock is null) return EmptyBatchContext.Instance;

        try
        {
            var messages = await FetchMessagesAsync(ct);

            if (messages.Count > 0)
            {
                return new BatchContext(messages, _completer, _hasNext, @lock);
            }

            await @lock.DisposeAsync();
            return EmptyBatchContext.Instance;
        }
        catch (Exception)
        {
            await @lock.DisposeAsync();
            throw;
        }
    }

    private async Task<IReadOnlyCollection<IMessage>> FetchMessagesAsync(CancellationToken ct)
    {
        var messages = await _collection.Find(_findFilter).Sort(_sort).Limit(_batchSize).ToListAsync(ct);
        var cast = new IMessage[messages.Count];
        for (var i = 0; i < messages.Count; i++)
        {
            cast[i] = messages[i];
        }

        return cast;
    }

    private Task<bool> HasNextAsync(CancellationToken ct) => _collection.Find(_findFilter).Limit(1).AnyAsync(ct);

    private class BatchContext(
        IReadOnlyCollection<IMessage> messages,
        BatchCompleter<TMessage, TId> completer,
        Func<CancellationToken, Task<bool>> hasNext,
        IDistributedLock @lock) : IBatchContext
    {
        public IReadOnlyCollection<IMessage> Messages => messages;

        public Task CompleteAsync(IReadOnlyCollection<IMessage> ok, CancellationToken ct)
            => completer.CompleteAsync(ok, ct);

        public Task<bool> HasNextAsync(CancellationToken ct) => hasNext(ct);

        public ValueTask DisposeAsync() => @lock.DisposeAsync();
    }
}