namespace YakShaveFx.OutboxKit.Core.Polling;

/// <summary>
/// Interface to be implemented by library users, to make it possible to retry completing messages already produced.
/// </summary>
public interface IBatchCompleteRetrier
{
    /// <summary>
    /// Retries completing the given collection of messages.
    /// </summary>
    /// <param name="messages">The messages that were previously successfully produced.</param>
    /// <param name="ct">The async cancellation token.</param>
    /// <returns>The task representing the asynchronous operation</returns>
    Task RetryAsync(IReadOnlyCollection<IMessage> messages, CancellationToken ct);
}