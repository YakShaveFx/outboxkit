namespace YakShaveFx.OutboxKit.Core.Polling;

public interface IBatchFetcher
{
    Task<IBatchContext> FetchAndHoldAsync(CancellationToken ct);
}

public interface IBatchContext : IAsyncDisposable
{
    /// <summary>
    /// The batch messages.
    /// </summary>
    IReadOnlyCollection<IMessage> Messages { get; }

    /// <summary>
    /// <para>Completes the batch.</para>
    /// <para>Messages that are not included in the <paramref name="ok"/> should be assumed to not have been produced, so should be kept in the outbox for posterior retry.</para>
    /// </summary>
    /// <param name="ok">The messages that were successfully produced.</param>
    /// <param name="ct">The async cancellation token.</param>
    /// <returns>The task representing the asynchronous operation</returns>
    Task CompleteAsync(IReadOnlyCollection<IMessage> ok, CancellationToken ct);

    /// <summary>
    ///  Indicates if there are more messages to fetch.
    /// </summary>
    /// <param name="ct">The async cancellation token.</param>
    /// <returns><code>true</code> if there are more messages to fetch, <code>false</code> otherwise.</returns>
    Task<bool> HasNextAsync(CancellationToken ct);
}

public sealed class EmptyBatchContext : IBatchContext
{
    private static readonly Task<bool> NoNext = Task.FromResult(false);
    
    private EmptyBatchContext()
    {
    }

    public static EmptyBatchContext Instance { get; } = new();
    public IReadOnlyCollection<IMessage> Messages => [];
    public Task CompleteAsync(IReadOnlyCollection<IMessage> ok, CancellationToken ct) => Task.CompletedTask;
    public Task<bool> HasNextAsync(CancellationToken ct) => NoNext;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}