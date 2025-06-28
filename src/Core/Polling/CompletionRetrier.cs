using System.Diagnostics;
using YakShaveFx.OutboxKit.Core.OpenTelemetry;

namespace YakShaveFx.OutboxKit.Core.Polling;

internal interface ICompletionRetryCollector
{
    void Collect(IReadOnlyCollection<IMessage> messages);
}

internal interface ICompletionRetrier
{
    ValueTask RetryAsync(CancellationToken ct);
}

// not thread safe, as it is only used in the context of a producing flow, which has no concurrency
internal sealed class CompletionRetrier(
    OutboxKey key,
    IBatchCompleteRetrier providerCompletionRetrier,
    RetrierBuilderFactory retrierBuilderFactory,
    CompletionRetrierMetrics metrics)
    : ICompletionRetryCollector, ICompletionRetrier
{
    private readonly Retrier _retrier = retrierBuilderFactory.Create()
        .WithMaxRetries(int.MaxValue)
        .WithShouldRetryDecider(ex =>
        {
            // retry on all exceptions except cancellation
            if (ex is OperationCanceledException oce) return oce.CancellationToken == CancellationToken.None;
            return true;
        })
        .Build();
    
    private List<IMessage> _messages = [];

    public void Collect(IReadOnlyCollection<IMessage> messages) => _messages.AddRange(messages);

    public ValueTask RetryAsync(CancellationToken ct)
        => _messages.Count == 0
            ? ValueTask.CompletedTask
            : new(InnerRetryCompleteAsync(ct));

    private async Task InnerRetryCompleteAsync(CancellationToken ct)
    {
        await _retrier.ExecuteWithRetryAsync(
            async () =>
            {
                metrics.CompletionRetryAttempted(key, _messages.Count);
                using var activity = ActivityHelpers.StartActivity(
                    "retrying produced messages completion",
                    key,
                    [new(ActivityConstants.OutboxProducedMessagesToCompleteTag, _messages.Count)]);
                
                try
                {
                    await providerCompletionRetrier.RetryAsync(_messages, ct);
                }
                catch (Exception)
                {
                    activity?.SetStatus(ActivityStatusCode.Error);
                    throw;
                }
            },
            ct);

        // since most of the time there are no messages to retry, we clear messages by creating a new list,
        // so the old one can be garbage collected, avoiding the underlying array to be kept in memory
        _messages = [];
    }
}