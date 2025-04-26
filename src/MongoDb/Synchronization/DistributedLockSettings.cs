namespace YakShaveFx.OutboxKit.MongoDb.Synchronization;

internal sealed record DistributedLockSettings
{
    public const string DefaultCollectionName = "outbox_locks";
    
    public string CollectionName { get; init; } = DefaultCollectionName; 
    public bool ChangeStreamsEnabled { get; init; } = true;
}