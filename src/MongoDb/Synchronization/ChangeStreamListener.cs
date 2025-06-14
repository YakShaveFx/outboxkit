using MongoDB.Driver;
using Nito.AsyncEx;

namespace YakShaveFx.OutboxKit.MongoDb.Synchronization;

internal sealed class ChangeStreamListener(
    AsyncAutoResetEvent autoResetEvent,
    IChangeStreamCursor<ChangeStreamDocument<DistributedLockDocument>> cursor,
    CancellationTokenSource cts) : IAsyncDisposable
{
    public Task WaitAsync() => autoResetEvent.WaitAsync(cts.Token);

    public static async Task<ChangeStreamListener> StartAsync(
        IMongoCollection<DistributedLockDocument> collection,
        DistributedLockDefinition lockDefinition,
        CancellationToken ct)
    {
        var cursor = await collection.WatchAsync(
            PipelineDefinitionBuilder
                .For<ChangeStreamDocument<DistributedLockDocument>>()
                .Match(d => d.DocumentKey["_id"] == lockDefinition.Id),
            new ChangeStreamOptions
            {
                BatchSize = 1,
                MaxAwaitTime = TimeSpan.FromMinutes(5)
            },
            ct);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var autoResetEvent = new AsyncAutoResetEvent();
        _ = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested && await cursor.MoveNextAsync(cts.Token))
            {
                // only yield when a change is detected, don't care about the amount, just that there is a change
                if (cursor.Current.Any())
                {
                    autoResetEvent.Set();
                }
            }
        }, cts.Token);

        var watcher = new ChangeStreamListener(autoResetEvent, cursor, cts);
        return watcher;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await cts.CancelAsync();
        }
        catch (Exception)
        {
            // try to cancel, but don't throw if it fails
        }

        cts.Dispose();
        cursor.Dispose();
    }
}