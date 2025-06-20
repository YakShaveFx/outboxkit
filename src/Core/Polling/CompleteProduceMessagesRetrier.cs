namespace YakShaveFx.OutboxKit.Core.Polling;

internal interface ICollectProducedMessagesToRetryCompletion
{
    void Collect(IReadOnlyCollection<IMessage> messages);
}

internal interface IRetryCompletionOfProducedMessages
{
    ValueTask RetryCompleteAsync(CancellationToken ct);
}

// not thread safe, as it is only used in the context of a producing flow, which has no concurrency
internal sealed class CompleteProduceMessagesRetrier(
    IProducedMessagesCompletionRetrier completionRetrier,
    RetrierBuilderFactory retrierBuilderFactory)
    : ICollectProducedMessagesToRetryCompletion, IRetryCompletionOfProducedMessages
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
    
    private List<IMessage> _messages = new();

    public void Collect(IReadOnlyCollection<IMessage> messages) => _messages.AddRange(messages);

    public ValueTask RetryCompleteAsync(CancellationToken ct)
        => _messages.Count == 0
            ? ValueTask.CompletedTask
            : new(InnerRetryCompleteAsync(ct));

    private async Task InnerRetryCompleteAsync(CancellationToken ct)
    {
        await _retrier.ExecuteWithRetryAsync(
            () => completionRetrier.RetryCompleteAsync(_messages, ct),
            ct);

        // since most of the time there are no messages to retry, we clear messages by creating a new list,
        // so the old one can be garbage collected, avoiding the underlying array to be kept in memory
        _messages = new();
    }
}