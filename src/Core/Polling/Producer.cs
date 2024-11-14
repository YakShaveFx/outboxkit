using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using YakShaveFx.OutboxKit.Core.OpenTelemetry;

namespace YakShaveFx.OutboxKit.Core.Polling;

// yes, this interface was created just to allow for using a test double, was the simpler thing I could come up with
internal interface IProducer
{
    Task ProducePendingAsync(string key, CancellationToken ct);
}

internal sealed class Producer(IServiceScopeFactory serviceScopeFactory) : IProducer
{
    public async Task ProducePendingAsync(string key, CancellationToken ct)
    {
        // Invokes ProduceBatchAsync while batches are being produce, to exhaust all pending messages.

        // ReSharper disable once EmptyEmbeddedStatement - the logic is part of the method invoked in the condition 
        while (!ct.IsCancellationRequested && await ProduceBatchAsync(key, ct)) ;
    }

    // returns true if there is a new batch to produce, false otherwise
    private async Task<bool> ProduceBatchAsync(string key, CancellationToken ct)
    {
        using var activity = ActivityHelpers.StartActivity("produce outbox message batch", key);
        using var scope = serviceScopeFactory.CreateScope();
        var batchFetcher = scope.ServiceProvider.GetRequiredKeyedService<IOutboxBatchFetcher>(key);

        await using var batchContext = await batchFetcher.FetchAndHoldAsync(ct);

        var messages = batchContext.Messages;
        activity?.SetTag(ActivityConstants.OutboxBatchSizeTag, messages.Count);

        // if we got not messages, there either aren't messages available or are being produced concurrently
        // in either case, we can break the loop
        if (messages.Count <= 0) return false;

        var result = await ProduceBatchAsync(key, scope, messages, ct);

        BatchProduced(scope, key, messages.Count, result.Ok.Count);

        // messages already produced, try to ack them
        // not passing the actual cancellation token to try to complete the batch even if the application is shutting down
        await batchContext.CompleteAsync(result.Ok, CancellationToken.None);

        return await batchContext.HasNextAsync(ct);
    }

    private static Task<BatchProduceResult> ProduceBatchAsync(
        string key,
        IServiceScope scope,
        IReadOnlyCollection<IMessage> messages,
        CancellationToken ct)
    {
        var batchProducer = scope.ServiceProvider.GetRequiredService<IBatchProducer>();
        return batchProducer.ProduceAsync(key, messages, ct);
    }

    private static void BatchProduced(IServiceScope scope, string key, int batchSize, int producedCount)
    {
        var metrics = scope.ServiceProvider.GetRequiredService<ProducerMetrics>();
        metrics.BatchProduced(key, batchSize == producedCount);
        metrics.MessagesProduced(key, producedCount);
    }
}