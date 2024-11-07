using System.Collections.Frozen;
using Nito.AsyncEx;

namespace YakShaveFx.OutboxKit.Core.Polling;

internal sealed class Listener : IOutboxListener, IOutboxTrigger, IKeyedOutboxListener, IKeyedOutboxTrigger
{
    private readonly AsyncAutoResetEvent _autoResetEvent = new();

    public void OnNewMessages() => _autoResetEvent.Set();
    public Task WaitForMessagesAsync(CancellationToken ct) => _autoResetEvent.WaitAsync(ct);

    // for simplicity, implementing IKeyedOutboxListener and IKeyedOutboxTrigger as well, disregarding the key 
    public void OnNewMessages(string key) => _autoResetEvent.Set();
    public Task WaitForMessagesAsync(string key, CancellationToken ct) => _autoResetEvent.WaitAsync(ct);
}

internal sealed class KeyedListener(IEnumerable<string> keys) : IKeyedOutboxListener, IKeyedOutboxTrigger
{
    private readonly FrozenDictionary<string, AsyncAutoResetEvent> _autoResetEvents
        = keys.ToFrozenDictionary(key => key, _ => new AsyncAutoResetEvent());

    public void OnNewMessages(string key)
    {
        if (!_autoResetEvents.TryGetValue(key, out var autoResetEvent))
        {
            throw new ArgumentException($"Key {key} not found to trigger outbox message production", nameof(key));
        }

        autoResetEvent.Set();
    }

    public Task WaitForMessagesAsync(string key, CancellationToken ct)
        => _autoResetEvents.TryGetValue(key, out var autoResetEvent)
            ? autoResetEvent.WaitAsync(ct)
            : throw new ArgumentException($"Key {key} not found to wait for outbox messages", nameof(key));
}

public interface IOutboxListener
{
    Task WaitForMessagesAsync(CancellationToken ct);
}

public interface IOutboxTrigger
{
    void OnNewMessages();
}

public interface IKeyedOutboxListener
{
    Task WaitForMessagesAsync(string key, CancellationToken ct);
}

public interface IKeyedOutboxTrigger
{
    void OnNewMessages(string key);
}