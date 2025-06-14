namespace YakShaveFx.OutboxKit.MongoDb;

internal static class LooseExtensions
{
    public static ValueTask TryDisposeAsync(this IAsyncDisposable? disposable)
        => disposable?.DisposeAsync() ?? ValueTask.CompletedTask;
}