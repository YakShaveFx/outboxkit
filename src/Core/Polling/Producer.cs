using YakShaveFx.OutboxKit.Core.OpenTelemetry;

namespace YakShaveFx.OutboxKit.Core.Polling;

// yes, this interface was created just to allow for using a test double, was the simpler thing I could come up with
internal interface IPollingProducer
{
    Task ProducePendingAsync(CancellationToken ct);
}

internal sealed class PollingProducer(
    OutboxKey key,
    IBatchFetcher fetcher,
    IBatchProducer producer,
    ProducerMetrics metrics) : IPollingProducer
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

        // messages already produced, try to ack them
        // not passing the actual cancellation token to try to complete the batch even if the application is shutting down
        await batchContext.CompleteAsync(result.Ok, CancellationToken.None);

        return await batchContext.HasNextAsync(ct);
    }
}