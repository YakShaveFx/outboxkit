using System.Linq.Expressions;
using MongoDB.Driver;
using YakShaveFx.OutboxKit.Core;
using YakShaveFx.OutboxKit.Core.Polling;
using YakShaveFx.OutboxKit.MongoDb.Synchronization;

namespace YakShaveFx.OutboxKit.MongoDb.Polling;

// ReSharper disable once ClassNeverInstantiated.Global - automagically instantiated by DI
internal sealed class OutboxBatchFetcher<TMessage, TId> : IBatchFetcher where TMessage : IMessage
{
    private readonly IMongoCollection<TMessage> _collection;
    private readonly DistributedLockThingy _lockThingy;
    private readonly TimeProvider _timeProvider;

    private readonly int _batchSize;
    private readonly string _lockId;
    private readonly string _lockOwner;
    private readonly string _lockContext;
    private readonly FilterDefinition<TMessage> _findFilter;
    private readonly SortDefinition<TMessage> _sort;
    private readonly Expression<Func<TMessage, TId>> _idSelector;
    private readonly Func<IMessage, TId> _idGetter;
    private readonly Expression<Func<TMessage, DateTime?>>? _processedAtSelector;
    private readonly Func<IReadOnlyCollection<IMessage>, CancellationToken, Task> _complete;
    private readonly Func<CancellationToken, Task<bool>> _hasNext;

    public OutboxBatchFetcher(
        OutboxKey key,
        MongoDbPollingSettings pollingSettings,
        MongoDbPollingCollectionSettings<TMessage, TId> collectionSettings,
        MongoDbPollingDistributedLockSettings lockSettings,
        IMongoDatabase db,
        DistributedLockThingy lockThingy,
        TimeProvider timeProvider)
    {
        _lockId = lockSettings.Id;
        _lockOwner = lockSettings.Owner;
        _lockContext = key.ToString();
        _lockThingy = lockThingy;
        _timeProvider = timeProvider;
        _batchSize = pollingSettings.BatchSize;
        _findFilter = (pollingSettings.CompletionMode, collectionSettings.ProcessedAtSelector) switch
        {
            (CompletionMode.Delete, _) => Builders<TMessage>.Filter.Empty,
            (CompletionMode.Update, not null) =>
                Builders<TMessage>.Filter.Eq(collectionSettings.ProcessedAtSelector, null),
            _ => throw new ArgumentException("processed at selector not configured", nameof(collectionSettings))
        };
        _sort = collectionSettings.Sort;
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
                return new BatchContext(messages, _complete, _hasNext, @lock);
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

    private async Task CompleteDeleteAsync(IReadOnlyCollection<IMessage> ok, CancellationToken ct)
    {
        if (ok.Count <= 0) return;

        using var session = await _collection.Database.Client.StartSessionAsync(new ClientSessionOptions(), ct);
        session.StartTransaction();
        
        var result = await _collection.DeleteManyAsync(
            session,
            Builders<TMessage>.Filter.In(_idSelector, ok.Select(_idGetter)),
            new DeleteOptions(),
            ct);

        if (result.DeletedCount != ok.Count) throw new InvalidOperationException("Failed to delete all messages");

        await session.CommitTransactionAsync(ct);
    }

    private async Task CompleteUpdateAsync(IReadOnlyCollection<IMessage> ok, CancellationToken ct)
    {
        if (ok.Count <= 0) return;

        using var session = await _collection.Database.Client.StartSessionAsync(new ClientSessionOptions(), ct);
        session.StartTransaction();

        var now = _timeProvider.GetUtcNow().DateTime;
        var result = await _collection.UpdateManyAsync(
            session,
            Builders<TMessage>.Filter.In(_idSelector, ok.Select(_idGetter)),
            Builders<TMessage>.Update.Set(_processedAtSelector, now),
            new UpdateOptions(),
            ct);

        if (result.ModifiedCount != ok.Count) throw new InvalidOperationException("Failed to update all messages");

        await session.CommitTransactionAsync(ct);
    }

    private Task<bool> HasNextAsync(CancellationToken ct) => _collection.Find(_findFilter).Limit(1).AnyAsync(ct);

    private class BatchContext(
        IReadOnlyCollection<IMessage> messages,
        Func<IReadOnlyCollection<IMessage>, CancellationToken, Task> complete,
        Func<CancellationToken, Task<bool>> hasNext,
        IDistributedLock @lock) : IBatchContext
    {
        public IReadOnlyCollection<IMessage> Messages => messages;

        public Task CompleteAsync(IReadOnlyCollection<IMessage> ok, CancellationToken ct) => complete(ok, ct);

        public Task<bool> HasNextAsync(CancellationToken ct) => hasNext(ct);

        public ValueTask DisposeAsync() => @lock.DisposeAsync();
    }
}