using YakShaveFx.OutboxKit.Core.Polling;

internal sealed class NoOpTrigger : IOutboxTrigger
{
    public void OnNewMessages()
    {
    }
}