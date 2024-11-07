namespace YakShaveFx.OutboxKit.Core.Polling;

public sealed record CorePollingSettings
{
    public TimeSpan PollingInterval { get; init; } = TimeSpan.FromMinutes(5);
}