namespace YakShaveFx.OutboxKit.Core;

/// <summary>
/// Interface to be implemented by library users, to make the bridge between the outbox and the message broker.
/// </summary>
public interface IBatchProducer
{
    /// <summary>
    /// Produces a batch of messages to the message broker.
    /// </summary>
    /// <param name="key">The key assigned to the outbox instance that's invoking this producer.</param>
    /// <param name="messages">The messages to produce.</param>
    /// <param name="ct">The async cancellation token.</param>
    /// <returns>A <see cref="BatchProduceResult"/> with information about the message production execution.</returns>
    Task<BatchProduceResult> ProduceAsync(OutboxKey key, IReadOnlyCollection<IMessage> messages, CancellationToken ct);
}

/// <summary>
/// Represents the result of a batch message production execution.
/// </summary>
public sealed class BatchProduceResult
{
    /// <summary>
    /// The messages that were successfully produced.
    /// </summary>
    /// <remarks>
    /// <para>Note that depending on the provider implementation, it might not be possible to complete the messages in cases of partial success,
    /// particularly if not matching the sequence in which they were provided to be produced.</para>
    /// <para>Look closely at the provider docs for information on potential issues.</para>
    /// </remarks>
    public required IReadOnlyCollection<IMessage> Ok { get; init; }
}
