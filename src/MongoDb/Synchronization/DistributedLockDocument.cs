namespace YakShaveFx.OutboxKit.MongoDb.Synchronization;

internal sealed record DistributedLockDocument
{
    public required string Id { get; init; }
    public required string Owner { get; init; }
    public required long ExpiresAt { get; init; }
}