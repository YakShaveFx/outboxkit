namespace YakShaveFx.OutboxKit.Core.CleanUp;

/// <summary>
/// To be implemented by database specific providers, in order to clean up processed messages from the outbox. 
/// </summary>
public interface IOutboxCleaner
{
    /// <summary>
    /// Cleans up processed messages from the outbox.
    /// </summary>
    /// <param name="ct">The async cancellation token.</param>
    /// <returns>The amount of messages cleaned up.</returns>
    Task<int> CleanAsync(CancellationToken ct);
}