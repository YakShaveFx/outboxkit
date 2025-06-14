using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace YakShaveFx.OutboxKit.MongoDb.Synchronization;

internal sealed partial class DistributedLockThingy(
    DistributedLockSettings settings,
    IMongoDatabase database,
    TimeProvider timeProvider,
    ILogger<DistributedLockThingy> logger)
{
    private readonly IMongoCollection<DistributedLockDocument> _collection =
        database.GetCollection<DistributedLockDocument>(settings.CollectionName);

    private readonly bool _changeStreamsEnabled = settings.ChangeStreamsEnabled;

    public async Task<IDistributedLock> AcquireAsync(DistributedLockDefinition lockDefinition, CancellationToken ct)
    {
        await KeepTryingToAcquireAsync(lockDefinition, ct);
        var keepAliveCts = new CancellationTokenSource();
        OnAcquired(lockDefinition, keepAliveCts.Token);
        return new DistributedLock(lockDefinition, keepAliveCts, ReleaseLockAsync);
    }

    public async Task<IDistributedLock?> TryAcquireAsync(DistributedLockDefinition lockDefinition, CancellationToken ct)
    {
        if (!await InnerTryAcquireAsync(lockDefinition, ct))
        {
            return null;
        }

        var keepAliveCts = new CancellationTokenSource();
        OnAcquired(lockDefinition, keepAliveCts.Token);
        return new DistributedLock(lockDefinition, keepAliveCts, ReleaseLockAsync);
    }

    private async ValueTask ReleaseLockAsync(
        DistributedLockDefinition lockDefinition,
        CancellationTokenSource keepAliveCts)
    {
        // try to release the lock, so others can acquire it before expiration

        try
        {
            // cancel the keep alive task
            await keepAliveCts.CancelAsync();

            var result = await _collection.DeleteOneAsync(
                Builders<DistributedLockDocument>.Filter.And(
                    Builders<DistributedLockDocument>.Filter.Eq(d => d.Id, lockDefinition.Id),
                    Builders<DistributedLockDocument>.Filter.Eq(d => d.Owner, lockDefinition.Owner)));

            if (result.DeletedCount == 1)
            {
                LogReleased(logger, lockDefinition.Id, lockDefinition.Context);
            }
        }
        catch (Exception ex)
        {
            LogReleaseFailed(logger, ex, lockDefinition.Id, lockDefinition.Context);
        }
        finally
        {
            keepAliveCts.Dispose();
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
        catch (Exception ex)
        {
            LogErrorAcquiringLock(logger, ex, lockDefinition.Id, lockDefinition.Context);
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
        if (!_changeStreamsEnabled)
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
                await Task.Delay(remaining, timeProvider, ct);
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
        => _ = Task.Run(async () =>
        {
            var keepAliveInterval = lockDefinition.Duration / 2;
            if (!_changeStreamsEnabled)
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(keepAliveInterval, timeProvider, ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        LogErrorExecutingKeepAlive(logger, ex, lockDefinition.Id, lockDefinition.Context);
                    }

                    if (!await InnerTryAcquireAsync(lockDefinition, ct))
                    {
                        OnLost(lockDefinition);
                        break;
                    }

                    LogExtended(logger, lockDefinition.Id, lockDefinition.Context);
                }
            }
            else
            {
                while (!ct.IsCancellationRequested)
                {
                    Task? watchLockLossTask = null;
                    using (var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        try
                        {
                            var delayTask = Task.Delay(keepAliveInterval, timeProvider, linkedTokenSource.Token);
                            watchLockLossTask = WatchForPotentialLockLossAsync(lockDefinition, linkedTokenSource.Token);
                            await Task.WhenAny(delayTask, watchLockLossTask);

                            // if awaiting was interrupted by cancellation, we can break
                            if (ct.IsCancellationRequested) break;
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            LogErrorExecutingKeepAlive(logger, ex, lockDefinition.Id, lockDefinition.Context);
                        }

                        await linkedTokenSource.CancelAsync();
                    }

                    if (!await InnerTryAcquireAsync(lockDefinition, ct))
                    {
                        OnLost(lockDefinition);
                        break;
                    }

                    if (watchLockLossTask?.IsCompletedSuccessfully ?? false)
                    {
                        LogReacquired(logger, lockDefinition.Id, lockDefinition.Context);
                    }
                    else
                    {
                        LogExtended(logger, lockDefinition.Id, lockDefinition.Context);
                    }
                }
            }

            LogKeepAliveStopped(logger, lockDefinition.Id, lockDefinition.Context);
        }, CancellationToken.None);

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
                LogPotentiallyLost(logger, lockDefinition.Id, lockDefinition.Context);
                return;
            }
        }
    }

    private void OnAcquired(DistributedLockDefinition lockDefinition, CancellationToken ct)
    {
        LogAcquired(logger, lockDefinition.Id, lockDefinition.Context);
        KickoffKeepAlive(lockDefinition, ct);
    }

    private void OnLost(DistributedLockDefinition lockDefinition)
    {
        LogLost(logger, lockDefinition.Id, lockDefinition.Context);
        _ = Task.Run(() => lockDefinition.OnLockLost());
    }

    private long GetNow() => timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

    private long GetExpiresAt(TimeSpan duration) => timeProvider.GetUtcNow().Add(duration).ToUnixTimeMilliseconds();

    private TimeSpan GetRemainingTime(DistributedLockDefinition lockDefinition, long expiresAt)
    {
        var remaining = expiresAt - timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        return remaining > 0
            ? TimeSpan.FromMilliseconds(Math.Min(remaining, lockDefinition.Duration.TotalMilliseconds))
            : TimeSpan.Zero;
    }

    [LoggerMessage(LogLevel.Debug, Message = "Lock acquired (id \"{Id}\" context \"{Context}\")")]
    private static partial void LogAcquired(ILogger logger, string id, string? context);


    [LoggerMessage(LogLevel.Warning,
        Message = "An error occurred while acquiring lock (id \"{Id}\" context \"{Context}\")")]
    private static partial void LogErrorAcquiringLock(ILogger logger, Exception ex, string id, string? context);

    [LoggerMessage(LogLevel.Debug, Message = "Lock ownership extended (id \"{Id}\" context \"{Context}\")")]
    private static partial void LogExtended(ILogger logger, string id, string? context);

    [LoggerMessage(LogLevel.Debug, Message = "Lock released (id \"{Id}\" context \"{Context}\")")]
    private static partial void LogReleased(ILogger logger, string id, string? context);

    [LoggerMessage(LogLevel.Warning, Message = "Failed to release lock (id \"{Id}\" context \"{Context}\")")]
    private static partial void LogReleaseFailed(ILogger logger, Exception ex, string id, string? context);

    [LoggerMessage(LogLevel.Debug,
        Message = "Lock potentially lost, trying to reacquire (id \"{Id}\" context \"{Context}\")")]
    private static partial void LogPotentiallyLost(ILogger logger, string id, string? context);

    [LoggerMessage(LogLevel.Debug, Message = "Lock reacquired (id \"{Id}\" context \"{Context}\")")]
    private static partial void LogReacquired(ILogger logger, string id, string? context);

    [LoggerMessage(LogLevel.Debug, Message = "Lock lost (id \"{Id}\" context \"{Context}\")")]
    private static partial void LogLost(ILogger logger, string id, string? context);

    [LoggerMessage(LogLevel.Debug, Message = "Lock keep alive stopped (id \"{Id}\" context \"{Context}\")")]
    private static partial void LogKeepAliveStopped(ILogger logger, string id, string? context);

    [LoggerMessage(LogLevel.Warning,
        Message = "An error occurred executing lock keep alive (id \"{Id}\" context \"{Context}\")")]
    private static partial void LogErrorExecutingKeepAlive(ILogger logger, Exception ex, string id, string? context);


    private sealed class DistributedLock(
        DistributedLockDefinition definition,
        CancellationTokenSource keepAliveCts,
        Func<DistributedLockDefinition, CancellationTokenSource, ValueTask> releaseLock) : IDistributedLock
    {
        public ValueTask DisposeAsync() => releaseLock(definition, keepAliveCts);
    }
}