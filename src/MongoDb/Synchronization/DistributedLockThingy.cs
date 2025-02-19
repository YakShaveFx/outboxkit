using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace YakShaveFx.OutboxKit.MongoDb.Synchronization;

internal sealed partial class DistributedLockThingy
{
    private readonly DistributedLockSettings _settings;
    private readonly IMongoCollection<DistributedLockDocument> _collection;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DistributedLockThingy> _logger;

    public DistributedLockThingy(
        DistributedLockSettings settings,
        IMongoDatabase database,
        TimeProvider timeProvider,
        ILogger<DistributedLockThingy> logger)
    {
        _settings = settings;
        _collection = database.GetCollection<DistributedLockDocument>(_settings.CollectionName);
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<IDistributedLock> AcquireAsync(DistributedLockDefinition lockDefinition, CancellationToken ct)
    {
        await KeepTryingToAcquireAsync(lockDefinition, ct);
        OnAcquired(lockDefinition, ct);
        return new DistributedLock(lockDefinition, ReleaseLockAsync);
    }

    public async Task<IDistributedLock?> TryAcquireAsync(DistributedLockDefinition lockDefinition, CancellationToken ct)
    {
        if (!await InnerTryAcquireAsync(lockDefinition, ct))
        {
            return null;
        }

        OnAcquired(lockDefinition, ct);
        return new DistributedLock(lockDefinition, ReleaseLockAsync);
    }

    private async ValueTask ReleaseLockAsync(DistributedLockDefinition lockDefinition)
    {
        // try to release the lock, so others can acquire it before expiration

        try
        {
            var result = await _collection.DeleteOneAsync(
                Builders<DistributedLockDocument>.Filter.And(
                    Builders<DistributedLockDocument>.Filter.Eq(d => d.Id, lockDefinition.Id),
                    Builders<DistributedLockDocument>.Filter.Eq(d => d.Owner, lockDefinition.Owner)));

            if (result.DeletedCount == 1)
            {
                LogReleased(_logger, lockDefinition.Id, lockDefinition.Context);
            }
        }
        catch (Exception ex)
        {
            LogReleaseFailed(_logger, ex, lockDefinition.Id, lockDefinition.Context);
        }
    }

    private async Task<bool> InnerTryAcquireAsync(DistributedLockDefinition lockDefinition, CancellationToken ct)
    {
        try
        {
            _ = await _collection.ReplaceOneAsync(
                GetUpsertFilter(lockDefinition),
                new DistributedLockDocument
                {
                    Id = lockDefinition.Id,
                    Owner = lockDefinition.Owner,
                    ExpiresAt = GetExpiresAt(lockDefinition.Duration)
                },
                new ReplaceOptions { IsUpsert = true },
                ct);

            return true;
        }
        // TODO: would be nice to be able to do this without exceptions, not sure it's possible though, needs investigation
        catch (MongoWriteException mwex) when (mwex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    private FilterDefinition<DistributedLockDocument> GetUpsertFilter(DistributedLockDefinition lockDefinition)
        => Builders<DistributedLockDocument>.Filter.Or(
            Builders<DistributedLockDocument>.Filter.And(
                Builders<DistributedLockDocument>.Filter.Eq(d => d.Id, lockDefinition.Id),
                Builders<DistributedLockDocument>.Filter.Eq(d => d.Owner, lockDefinition.Owner)),
            Builders<DistributedLockDocument>.Filter.And(
                Builders<DistributedLockDocument>.Filter.Eq(d => d.Id, lockDefinition.Id),
                Builders<DistributedLockDocument>.Filter.Lt(d => d.ExpiresAt, GetNow())));

    private async Task KeepTryingToAcquireAsync(DistributedLockDefinition lockDefinition, CancellationToken ct)
    {
        if (!_settings.ChangeStreamsEnabled)
        {
            await PollAndKeepTryingToAcquireAsync(lockDefinition, ct);
            return;
        }

        using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var linkedCt = linkedTokenSource.Token;

        var pollingTask = PollAndKeepTryingToAcquireAsync(lockDefinition, linkedCt);
        var changeStreamTask = WatchAndKeepTryingToAcquireAsync(lockDefinition, linkedCt);

        // TODO: check if there are some exceptions worth handling here
        await Task.WhenAny(changeStreamTask, pollingTask);
        await linkedTokenSource.CancelAsync();
    }

    private async Task PollAndKeepTryingToAcquireAsync(DistributedLockDefinition lockDefinition, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var current = await _collection
                .Find(Builders<DistributedLockDocument>.Filter.Eq(d => d.Id, lockDefinition.Id))
                .FirstOrDefaultAsync(ct);
            var remaining = GetRemainingTime(lockDefinition, current?.ExpiresAt ?? 0);
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, _timeProvider, ct);
            }

            if (await InnerTryAcquireAsync(lockDefinition, ct)) return;
        }
    }

    private async Task WatchAndKeepTryingToAcquireAsync(DistributedLockDefinition lockDefinition, CancellationToken ct)
    {
        using var cursor = await _collection.WatchAsync(
            PipelineDefinitionBuilder
                .For<ChangeStreamDocument<DistributedLockDocument>>()
                .Match(d =>
                    d.OperationType == ChangeStreamOperationType.Delete && d.DocumentKey["_id"] == lockDefinition.Id),
            new ChangeStreamOptions
            {
                BatchSize = 1,
                MaxAwaitTime = TimeSpan.FromMinutes(5)
            },
            ct);

        while (!ct.IsCancellationRequested && await cursor.MoveNextAsync(ct))
        {
            foreach (var _ in cursor.Current)
            {
                if (await InnerTryAcquireAsync(lockDefinition, ct)) return;
            }
        }
    }

    private void KickoffKeepAlive(DistributedLockDefinition lockDefinition, CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            var keepAliveInterval = lockDefinition.Duration / 2;
            if (!_settings.ChangeStreamsEnabled)
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(keepAliveInterval, _timeProvider, ct);
                    if (!await InnerTryAcquireAsync(lockDefinition, ct))
                    {
                        OnLost(lockDefinition);
                        return;
                    }

                    LogExtended(_logger, lockDefinition.Id, lockDefinition.Context);
                }
            }
            else
            {
                while (!ct.IsCancellationRequested)
                {
                    var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    var delayTask = Task.Delay(keepAliveInterval, _timeProvider,
                        linkedTokenSource.Token);
                    var watchLockLossTask = WatchForPotentialLockLossAsync(lockDefinition, linkedTokenSource.Token);
                    await Task.WhenAny(delayTask, watchLockLossTask);

                    await linkedTokenSource.CancelAsync();

                    if (!await InnerTryAcquireAsync(lockDefinition, ct))
                    {
                        OnLost(lockDefinition);
                        return;
                    }

                    if (watchLockLossTask.IsCompletedSuccessfully) LogReacquired(_logger, lockDefinition.Id, lockDefinition.Context);
                    else LogExtended(_logger, lockDefinition.Id, lockDefinition.Context);
                }
            }
        }, ct);
    }

    private async Task WatchForPotentialLockLossAsync(DistributedLockDefinition lockDefinition, CancellationToken ct)
    {
        using var cursor = await _collection.WatchAsync(
            PipelineDefinitionBuilder
                .For<ChangeStreamDocument<DistributedLockDocument>>()
                .Match(d => d.DocumentKey["_id"] == lockDefinition.Id),
            new ChangeStreamOptions
            {
                BatchSize = 1,
                MaxAwaitTime = TimeSpan.FromMinutes(5)
            },
            ct);

        while (!ct.IsCancellationRequested && await cursor.MoveNextAsync(ct))
        {
            if (cursor.Current.Any())
            {
                LogPotentiallyLost(_logger, lockDefinition.Id, lockDefinition.Context);
                return;
            }
        }
    }

    private void OnAcquired(DistributedLockDefinition lockDefinition, CancellationToken ct)
    {
        LogAcquired(_logger, lockDefinition.Id, lockDefinition.Context);
        KickoffKeepAlive(lockDefinition, ct);
    }

    private void OnLost(DistributedLockDefinition lockDefinition)
    {
        LogLost(_logger, lockDefinition.Id, lockDefinition.Context);
        _ = Task.Run(() => lockDefinition.OnLockLost());
    }

    private long GetNow() => _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

    private long GetExpiresAt(TimeSpan duration) => _timeProvider.GetUtcNow().Add(duration).ToUnixTimeMilliseconds();

    private TimeSpan GetRemainingTime(DistributedLockDefinition lockDefinition, long expiresAt)
    {
        var remaining = expiresAt - _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        return remaining > 0
            ? TimeSpan.FromMilliseconds(Math.Min(remaining, lockDefinition.Duration.TotalMilliseconds))
            : TimeSpan.Zero;
    }

    [LoggerMessage(LogLevel.Debug, Message = "Lock acquired (id \"{Id}\" context \"{Context}\")")]
    private static partial void LogAcquired(ILogger logger, string id, string? context);

    [LoggerMessage(LogLevel.Debug, Message = "Lock ownership extended (id \"{Id}\" context \"{Context}\")")]
    private static partial void LogExtended(ILogger logger, string id, string? context);

    [LoggerMessage(LogLevel.Debug, Message = "Lock released (id \"{Id}\" context \"{Context}\")")]
    private static partial void LogReleased(ILogger logger, string id, string? context);

    [LoggerMessage(LogLevel.Debug, Message = "Failed to release lock (id \"{Id}\" context \"{Context}\")")]
    private static partial void LogReleaseFailed(ILogger logger, Exception ex, string id, string? context);
    
    [LoggerMessage(LogLevel.Debug, Message = "Lock potentially lost, trying to reacquire (id \"{Id}\" context \"{Context}\")")]
    private static partial void LogPotentiallyLost(ILogger logger, string id, string? context);

    [LoggerMessage(LogLevel.Debug, Message = "Lock reacquired (id \"{Id}\" context \"{Context}\")")]
    private static partial void LogReacquired(ILogger logger, string id, string? context);

    [LoggerMessage(LogLevel.Debug, Message = "Lock lost (id \"{Id}\" context \"{Context}\")")]
    private static partial void LogLost(ILogger logger, string id, string? context);


    private sealed class DistributedLock(
        DistributedLockDefinition definition,
        Func<DistributedLockDefinition, ValueTask> releaseLock) : IDistributedLock
    {
        public ValueTask DisposeAsync() => releaseLock(definition);
    }
}