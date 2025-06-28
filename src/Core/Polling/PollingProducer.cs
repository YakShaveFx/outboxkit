using Microsoft.Extensions.Logging;
using YakShaveFx.OutboxKit.Core.OpenTelemetry;

namespace YakShaveFx.OutboxKit.Core.Polling;

// yes, this interface was created just to allow for using a test double, was the simpler thing I could come up with
internal interface IPollingProducer
{
    Task ProducePendingAsync(CancellationToken ct);
}

internal sealed partial class PollingProducer(
    OutboxKey key,
    IBatchFetcher fetcher,
    IBatchProducer producer,
    ICompletionRetryCollector completionRetryCollector,
    ProducerMetrics metrics,
    ILogger<PollingProducer> logger) : IPollingProducer
{
    public async Task ProducePendingAsync(CancellationToken ct)
    {
        // Invokes ProduceBatchAsync while batches are being produce, to exhaust all pending messages.

        // ReSharper disable once EmptyEmbeddedStatement - the logic is part of the method invoked in the condition 
        while (!ct.IsCancellationRequested && await ProduceBatchAsync(ct)) ;
    }

    // returns true if there is a new batch to produce, false otherwise
    private async Task<bool> ProduceBatchAsync(CancellationToken ct)
    {
        using var activity = ActivityHelpers.StartActivity("produce outbox message batch", key);

        await using var batchContext = await fetcher.FetchAndHoldAsync(ct);

        var messages = batchContext.Messages;
        activity?.SetTag(ActivityConstants.OutboxBatchSizeTag, messages.Count);

        // if we got not messages, there either aren't messages available or are being produced concurrently
        // in either case, we can break the loop
        if (messages.Count <= 0) return false;

        var result = await producer.ProduceAsync(key, messages, ct);
        
        metrics.BatchProduced(key, messages.Count == result.Ok.Count);
        metrics.MessagesProduced(key, result.Ok.Count);

        try
        {
            // messages already produced, try to ack them
            // not passing the actual cancellation token to try to complete the batch even if the application is shutting down
            await batchContext.CompleteAsync(result.Ok, CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogCompletionUnexpectedError(logger, key.ProviderKey, key.ClientKey, ex);
            completionRetryCollector.Collect(result.Ok);
            
            // return false to break the loop, as we don't want to produce more messages until we're able to complete the batch
            return false;
        }

        return await batchContext.HasNextAsync(ct);
    }
    
    [LoggerMessage(LogLevel.Error,
        Message =
            "Unexpected error while completing produced outbox messages for provider key \"{providerKey}\" and client key \"{clientKey}\"")]
    private static partial void LogCompletionUnexpectedError(ILogger logger, string providerKey, string clientKey, Exception ex);
}