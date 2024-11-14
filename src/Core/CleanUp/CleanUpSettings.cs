namespace YakShaveFx.OutboxKit.Core.CleanUp;

/// <summary>
/// Core clean up settings for the outbox.
/// </summary>
public sealed record CoreCleanUpSettings
{
    /// <summary>
    /// The interval at which the outbox is cleaned up of processed messages.
    /// </summary>
    public TimeSpan CleanUpInterval { get; init; } = TimeSpan.FromHours(1);
}
