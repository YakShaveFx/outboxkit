namespace YakShaveFx.OutboxKit.Core.Polling;

/// <summary>
/// Core polling settings for the outbox.
/// </summary>
public sealed record CorePollingSettings
{
    /// <summary>
    /// The interval at which the outbox is polled for new messages.
    /// </summary>
    public TimeSpan PollingInterval { get; init; } = TimeSpan.FromMinutes(5);
    
    /// <summary>
    /// Indicates if the outbox should enable the processed messages clean up task.
    /// </summary>
    public bool EnableCleanUp { get; init; } = false;
    
    /// <summary>
    /// The interval at which the outbox is cleaned up of processed messages.
    /// </summary>
    public TimeSpan CleanUpInterval { get; init; } = TimeSpan.FromHours(1);
}