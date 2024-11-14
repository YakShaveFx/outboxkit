using System.Collections.Frozen;
using Nito.AsyncEx;

namespace YakShaveFx.OutboxKit.Core.Polling;

internal sealed class Listener : IOutboxListener, IOutboxTrigger, IKeyedOutboxListener, IKeyedOutboxTrigger
{
    private readonly AsyncAutoResetEvent _autoResetEvent = new();

    public void OnNewMessages() => _autoResetEvent.Set();
    public Task WaitForMessagesAsync(CancellationToken ct) => _autoResetEvent.WaitAsync(ct);

    // for simplicity, implementing IKeyedOutboxListener and IKeyedOutboxTrigger as well, disregarding the key 
    public void OnNewMessages(OutboxKey key) => _autoResetEvent.Set();
    public Task WaitForMessagesAsync(OutboxKey key, CancellationToken ct) => _autoResetEvent.WaitAsync(ct);
}

internal sealed class KeyedListener(IEnumerable<OutboxKey> keys) : IKeyedOutboxListener, IKeyedOutboxTrigger
{
    private readonly FrozenDictionary<OutboxKey, AsyncAutoResetEvent> _autoResetEvents
        = keys.ToFrozenDictionary(key => key, _ => new AsyncAutoResetEvent());

    public void OnNewMessages(OutboxKey key)
    {
        if (!_autoResetEvents.TryGetValue(key, out var autoResetEvent))
        {
            throw new ArgumentException($"Key {key} not found to trigger outbox message production", nameof(key));
        }

        autoResetEvent.Set();
    }

    public Task WaitForMessagesAsync(OutboxKey key, CancellationToken ct)
        => _autoResetEvents.TryGetValue(key, out var autoResetEvent)
            ? autoResetEvent.WaitAsync(ct)
            : throw new ArgumentException($"Key {key} not found to wait for outbox messages", nameof(key));
}

internal interface IOutboxListener
{
    Task WaitForMessagesAsync(CancellationToken ct);
}

internal interface IKeyedOutboxListener
{
    Task WaitForMessagesAsync(OutboxKey key, CancellationToken ct);
}

/// <summary>
/// Allows triggering the check for new outbox messages, without waiting for the polling interval.
/// </summary>
public interface IOutboxTrigger
{
    /// <summary>
    /// Triggers the check for new outbox messages, without waiting for the polling interval.
    /// </summary>
    void OnNewMessages();
}

/// <summary>
/// Allows triggering the check for new outbox messages for a specific key, without waiting for the polling interval.
/// </summary>
public interface IKeyedOutboxTrigger
{
    /// <summary>
    /// Triggers the check for new outbox messages for a specific key, without waiting for the polling interval.
    /// </summary>
    /// <param name="key">The key of the outbox to trigger.</param>
    void OnNewMessages(OutboxKey key);
}