namespace YakShaveFx.OutboxKit.MongoDb.Synchronization;

// wraps the DistributedLockDefinition and adds additional properties for internal use
// so it's easier to pass it around
internal sealed class InternalDistributedLockDefinition
{
    public required DistributedLockDefinition Definition { get; init; }
    public required ChangeStreamListener? ChangeStreamListener { get; init; }

    public string Id => Definition.Id;
    public string Owner => Definition.Owner;
    public string? Context => Definition.Context;
    public TimeSpan Duration => Definition.Duration;
    public Func<Task> OnLockLost => Definition.OnLockLost;
}