using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Nito.AsyncEx;

namespace YakShaveFx.OutboxKit.MongoDb.Synchronization;

internal interface IChangeStreamNotifier : IAsyncDisposable
{
    Task OnChangeAsync(CancellationToken ct);
}

internal sealed partial class ChangeStreamListener(ILogger<ChangeStreamListener> logger)
{
    public async Task<IChangeStreamNotifier> ListenAsync(
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
                BatchSize = 1
            },
            ct);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var autoResetEvent = new AsyncAutoResetEvent();
        var backgroundTask = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested && await cursor.MoveNextAsync(cts.Token))
                {
                    // only yield when a relevant change is detected, don't care about the amount, just that there is a relevant change
                    if (cursor.Current.Any(d => ShouldYield(d, lockDefinition, logger)))
                    {
                        autoResetEvent.Set();
                    }
                }
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                // expected when the cancellation token is canceled
            }
            catch (Exception ex)
            {
                // log and forget is only acceptable here,
                // because there always is a parallel process relying on delays to double-check things
                LogErrorWatchingForLockChanges(logger, ex, lockDefinition.Id, lockDefinition.Context);
            }
        }, cts.Token);

        var watcher = new ChangeStreamNotifier(autoResetEvent, cursor, cts, backgroundTask);
        return watcher;

        /*
         * we care if:
         * - any delete to the lock document while it should be up
         * - any insert or replace to the lock document while it should be up, but only if the owner is different
         * (otherwise we'd get notified by what the current lock is doing)
         */
        static bool ShouldYield(
            ChangeStreamDocument<DistributedLockDocument> document,
            DistributedLockDefinition lockDefinition,
            ILogger logger)
        {
            if (document.OperationType is ChangeStreamOperationType.Delete)
            {
                LogLockDeletion(logger, lockDefinition.Id, lockDefinition.Context);
                return true;
            }

            if (document.OperationType is ChangeStreamOperationType.Insert or ChangeStreamOperationType.Replace
                && document.FullDocument.Owner != lockDefinition.Owner)
            {
                LogLockChangeWithDifferentOwner(
                    logger,
                    document.OperationType,
                    lockDefinition.Owner,
                    document.FullDocument.Owner,
                    lockDefinition.Id,
                    lockDefinition.Context);

                return true;
            }

            LogIrrelevantLockChange(logger, document.OperationType, lockDefinition.Id, lockDefinition.Context);

            return false;
        }
    }

    private sealed class ChangeStreamNotifier(
        AsyncAutoResetEvent autoResetEvent,
        IChangeStreamCursor<ChangeStreamDocument<DistributedLockDocument>> cursor,
        CancellationTokenSource cts,
        Task backgroundTask) : IChangeStreamNotifier
    {
        public async Task OnChangeAsync(CancellationToken ct)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);
            await autoResetEvent.WaitAsync(linkedCts.Token);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await cts.CancelAsync();
                await backgroundTask;
            }
            catch (Exception)
            {
                // try to cancel and await the task, but don't throw if it fails
            }

            cursor.Dispose();
            cts.Dispose();
        }
    }

    [LoggerMessage(
        LogLevel.Debug,
        Message = "Lock deletion detected (id \"{Id}\" context \"{Context}\")")]
    private static partial void LogLockDeletion(ILogger logger, string? id, string? context);


    [LoggerMessage(
        LogLevel.Debug,
        Message =
            "Lock change detected with different owner (operation \"{OperationType}\" expected owner \"{ExpectedOwner}\" actual owner \"{ActualOwner}\" id \"{Id}\" context \"{Context}\")")]
    private static partial void LogLockChangeWithDifferentOwner(
        ILogger logger,
        ChangeStreamOperationType operationType,
        string expectedOwner,
        string? actualOwner,
        string id,
        string? context);

    [LoggerMessage(
        LogLevel.Debug,
        Message = "Irrelevant lock change detected (operation \"{OperationType}\" id \"{Id}\" context \"{Context}\")")]
    private static partial void LogIrrelevantLockChange(
        ILogger logger,
        ChangeStreamOperationType operationType,
        string id,
        string? context);

    [LoggerMessage(
        LogLevel.Warning,
        Message =
            "An error occurred while watching for lock changes, falling back to time based alternatives (id \"{Id}\" context \"{Context}\")")]
    private static partial void LogErrorWatchingForLockChanges(
        ILogger logger,
        Exception ex,
        string id,
        string? context);
}