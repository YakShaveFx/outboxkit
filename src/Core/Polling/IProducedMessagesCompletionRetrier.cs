namespace YakShaveFx.OutboxKit.Core.Polling;

/// <summary>
/// Interface to be implemented by library users, to make it possible to retry completing messages already produced.
/// </summary>
public interface IProducedMessagesCompletionRetrier
{
    /// <summary>
    /// <para>Completes the given collection of messages.</para>
    /// </summary>
    /// <param name="messages">The messages that were previously successfully produced.</param>
    /// <param name="ct">The async cancellation token.</param>
    /// <returns>The task representing the asynchronous operation</returns>
    Task RetryCompleteAsync(IReadOnlyCollection<IMessage> messages, CancellationToken ct);
}