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
}