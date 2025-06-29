using System.Diagnostics;
using Microsoft.Extensions.Logging;
using YakShaveFx.OutboxKit.Core.OpenTelemetry;

namespace YakShaveFx.OutboxKit.Core.Polling;

internal enum ProducePendingResult
{
    Ok,
    FetchError,
    ProduceError,
    PartialProduction,
    CompleteError
}

// yes, this interface was created just to allow for using a test double, was the simpler thing I could come up with
internal interface IPollingProducer
{
    Task<ProducePendingResult> ProducePendingAsync(CancellationToken ct);
}

internal sealed partial class PollingProducer(
    OutboxKey key,
    IBatchFetcher fetcher,
    IBatchProducer producer,
    ICompletionRetryCollector completionRetryCollector,
    PollingProducerMetrics metrics,
    ILogger<PollingProducer> logger) : IPollingProducer
{
    private enum ProduceBatchResult
    {
        MoreAvailable,
        AllDone,
        FetchError,
        ProduceError,
        PartialProduction,
        CompleteError
    }

    public async Task<ProducePendingResult> ProducePendingAsync(CancellationToken ct)
    {
        // invokes ProduceBatchAsync while batches are being produce, to exhaust all pending messages.

        while (!ct.IsCancellationRequested)
        {
            var result = await ProduceBatchAsync(ct);

            switch (result)
            {
                case ProduceBatchResult.MoreAvailable:
                    continue;
                case ProduceBatchResult.AllDone:
                    return ProducePendingResult.Ok;
                case ProduceBatchResult.FetchError:
                    return ProducePendingResult.FetchError;
                case ProduceBatchResult.ProduceError:
                    return ProducePendingResult.ProduceError;
                case ProduceBatchResult.CompleteError:
                    return ProducePendingResult.CompleteError;
                case ProduceBatchResult.PartialProduction:
                    return ProducePendingResult.PartialProduction;
                default:
                    throw new InvalidOperationException(
                        $"Unexpected result from {nameof(ProduceBatchAsync)}: {result}");
            }
        }

        // if we reach here, it means the cancellation token was triggered
        return ProducePendingResult.Ok;
    }

    // returns true if there is a new batch to produce, false otherwise
    private async Task<ProduceBatchResult> ProduceBatchAsync(CancellationToken ct)
    {
        using var activity = ActivityHelpers.StartActivity("produce outbox message batch", key);

        IBatchContext? batchContext = null;
        try
        {
            try
            {
                batchContext = await fetcher.FetchAndHoldAsync(ct);
            }
            catch (Exception ex)
            {
                LogFetchingUnexpectedError(logger, key.ProviderKey, key.ClientKey, ex);
                SetStatusAndRecordException(activity, ex);
                return ProduceBatchResult.FetchError;
            }

            BatchProduceResult? batchProduceResult;

            try
            {
                var messages = batchContext.Messages;
                activity?.SetTag(ActivityConstants.OutboxBatchSizeTag, messages.Count);

                // if we got not messages, there either aren't messages available or are being produced concurrently
                // in either case, we can break the loop
                if (messages.Count <= 0) return ProduceBatchResult.AllDone;

                batchProduceResult = await producer.ProduceAsync(key, messages, ct);

                metrics.BatchProduced(key, messages.Count == batchProduceResult.Ok.Count);
                metrics.MessagesProduced(key, batchProduceResult.Ok.Count);
            }
            catch (Exception ex)
            {
                LogProductionUnexpectedError(logger, key.ProviderKey, key.ClientKey, ex);
                SetStatusAndRecordException(activity, ex);
                return ProduceBatchResult.ProduceError;
            }

            try
            {
                // messages already produced, try to ack them
                // not passing the actual cancellation token to try to complete the batch even if the application is shutting down
                await batchContext.CompleteAsync(batchProduceResult.Ok, CancellationToken.None);
            }
            catch (Exception ex)
            {
                LogCompletionUnexpectedError(logger, key.ProviderKey, key.ClientKey, ex);
                SetStatusAndRecordException(activity, ex);
                completionRetryCollector.Collect(batchProduceResult.Ok);

                // return to break the loop, as we don't want to produce more messages until we're able to complete the batch
                return ProduceBatchResult.CompleteError;
            }

            if (batchProduceResult.Ok.Count < batchContext.Messages.Count)
            {
                // if we produced only a subset of the messages, something probably went wrong, so we want to handle it explicitly
                return ProduceBatchResult.PartialProduction;
            }

            try
            {
                return await batchContext.HasNextAsync(ct)
                    ? ProduceBatchResult.MoreAvailable
                    : ProduceBatchResult.AllDone;
            }
            catch (Exception ex)
            {
                // equivalent to a fetch error, as it's just querying the db, so the handling should be the same
                LogFetchingUnexpectedError(logger, key.ProviderKey, key.ClientKey, ex);
                SetStatusAndRecordException(activity, ex);
                return ProduceBatchResult.FetchError;
            }
        }
        finally
        {
            if (batchContext is not null)
            {
                await batchContext.DisposeAsync();
            }
        }
    }

    private void SetStatusAndRecordException(Activity? activity, Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error);
        activity?.RecordException(ex, new TagList
        {
            { ActivityConstants.OutboxProviderKeyTag, key.ProviderKey },
            { ActivityConstants.OutboxClientKeyTag, key.ClientKey }
        });
    }

    [LoggerMessage(LogLevel.Error,
        Message =
            "Unexpected error while fetching outbox messages for provider key \"{providerKey}\" and client key \"{clientKey}\"")]
    private static partial void LogFetchingUnexpectedError(ILogger logger, string providerKey, string clientKey,
        Exception ex);

    [LoggerMessage(LogLevel.Error,
        Message =
            "Unexpected error while producing outbox messages for provider key \"{providerKey}\" and client key \"{clientKey}\"")]
    private static partial void LogProductionUnexpectedError(ILogger logger, string providerKey, string clientKey,
        Exception ex);

    [LoggerMessage(LogLevel.Error,
        Message =
            "Unexpected error while completing produced outbox messages for provider key \"{providerKey}\" and client key \"{clientKey}\"")]
    private static partial void LogCompletionUnexpectedError(ILogger logger, string providerKey, string clientKey,
        Exception ex);
}