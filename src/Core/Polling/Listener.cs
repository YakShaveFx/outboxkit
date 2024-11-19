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

internal abstract class KeyedListener : IKeyedOutboxListener, IKeyedOutboxTrigger
{
    public abstract void OnNewMessages(OutboxKey key);
    public abstract Task WaitForMessagesAsync(OutboxKey key, CancellationToken ct);

    protected static ArgumentException KeyNotFoundForTriggerException(OutboxKey key)
        => new($"Key {key} not found to trigger outbox message production", nameof(key));

    protected static ArgumentException KeyNotFoundForWaitException(OutboxKey key)
        => new($"Key {key} not found to wait for outbox messages", nameof(key));

    public static KeyedListener Create(IReadOnlyCollection<OutboxKey> keys)
    {
        if (keys.Count == 0) throw new ArgumentException("No keys provided", nameof(keys));

        var providerCount = keys.Select(k => k.ProviderKey).Distinct().Count();
        var clientCount = keys.Select(k => k.ClientKey).Distinct().Count();

        return (providerCount, clientCount) switch
        {
            (1, 1) => throw new ArgumentException(
                $"Only one key provided, use the simpler {nameof(Listener)} instead",
                nameof(keys)),
            (> 1, 1) => new MultiProviderSingleClientKeyedListener(keys),
            (1, > 1) => new SingleProviderMultiClientKeyedListener(keys),
            _ => new MultiProviderMultiClientKeyedListener(keys)
        };
    }
}

internal sealed class MultiProviderSingleClientKeyedListener : KeyedListener
{
    private readonly string _clientKey;
    private readonly FrozenDictionary<string, AsyncAutoResetEvent> _autoResetEvents;

    public MultiProviderSingleClientKeyedListener(IReadOnlyCollection<OutboxKey> keys)
    {
        var providerCount = keys.Select(k => k.ProviderKey).Distinct().Count();
        var clientCount = keys.Select(k => k.ClientKey).Distinct().Count();

        if (providerCount <= 1 || clientCount != 1)
        {
            throw new ArgumentException("Expected multiple provider keys and a single client key", nameof(keys));
        }

        _clientKey = keys.First().ClientKey;
        _autoResetEvents = keys.GroupBy(k => k.ProviderKey)
            .ToFrozenDictionary(
                g => g.Key,
                _ => new AsyncAutoResetEvent());
    }


    public override void OnNewMessages(OutboxKey key)
    {
        if (!_autoResetEvents.TryGetValue(key.ProviderKey, out var autoResetEvent) || key.ClientKey != _clientKey)
        {
            throw KeyNotFoundForTriggerException(key);
        }

        autoResetEvent.Set();
    }

    public override Task WaitForMessagesAsync(OutboxKey key, CancellationToken ct)
    {
        if (!_autoResetEvents.TryGetValue(key.ProviderKey, out var autoResetEvent) || key.ClientKey != _clientKey)
        {
            throw KeyNotFoundForWaitException(key);
        }

        return autoResetEvent.WaitAsync(ct);
    }
}

internal sealed class SingleProviderMultiClientKeyedListener : KeyedListener
{
    private readonly string _providerKey;

    private readonly FrozenDictionary<string, AsyncAutoResetEvent> _autoResetEvents;

    public SingleProviderMultiClientKeyedListener(IReadOnlyCollection<OutboxKey> keys)
    {
        var providerCount = keys.Select(k => k.ProviderKey).Distinct().Count();
        var clientCount = keys.Select(k => k.ClientKey).Distinct().Count();

        if (providerCount != 1 || clientCount <= 1)
        {
            throw new ArgumentException("Expected single provider key and a multiple client keys", nameof(keys));
        }

        _providerKey = keys.First().ProviderKey;
        _autoResetEvents = keys.GroupBy(k => k.ClientKey)
            .ToFrozenDictionary(
                g => g.Key,
                _ => new AsyncAutoResetEvent());
    }

    public override void OnNewMessages(OutboxKey key)
    {
        if (!_autoResetEvents.TryGetValue(key.ClientKey, out var autoResetEvent) || key.ProviderKey != _providerKey)
        {
            throw KeyNotFoundForTriggerException(key);
        }

        autoResetEvent.Set();
    }

    public override Task WaitForMessagesAsync(OutboxKey key, CancellationToken ct)
    {
        if (!_autoResetEvents.TryGetValue(key.ClientKey, out var autoResetEvent) || key.ProviderKey != _providerKey)
        {
            throw KeyNotFoundForWaitException(key);
        }

        return autoResetEvent.WaitAsync(ct);
    }
}

internal sealed class MultiProviderMultiClientKeyedListener : KeyedListener
{
    private readonly FrozenDictionary<string, FrozenDictionary<string, AsyncAutoResetEvent>> _autoResetEvents;

    public MultiProviderMultiClientKeyedListener(IReadOnlyCollection<OutboxKey> keys)
    {
        var providerCount = keys.Select(k => k.ProviderKey).Distinct().Count();
        var clientCount = keys.Select(k => k.ClientKey).Distinct().Count();

        if (providerCount <= 1 || clientCount <= 1)
        {
            throw new ArgumentException("Expected multiple provider keys and a multiple client keys", nameof(keys));
        }

        _autoResetEvents = keys.GroupBy(k => k.ProviderKey)
            .ToFrozenDictionary(
                g => g.Key,
                g => g.ToFrozenDictionary(
                    k => k.ClientKey,
                    _ => new AsyncAutoResetEvent()));
    }

    public override void OnNewMessages(OutboxKey key)
    {
        if (!_autoResetEvents.TryGetValue(key.ProviderKey, out var clientKeys)
            || !clientKeys.TryGetValue(key.ClientKey, out var autoResetEvent))
        {
            throw KeyNotFoundForTriggerException(key);
        }

        autoResetEvent.Set();
    }

    public override Task WaitForMessagesAsync(OutboxKey key, CancellationToken ct)
    {
        if (!_autoResetEvents.TryGetValue(key.ProviderKey, out var clientKeys)
            || !clientKeys.TryGetValue(key.ClientKey, out var autoResetEvent))
        {
            throw KeyNotFoundForWaitException(key);
        }

        return autoResetEvent.WaitAsync(ct);
    }
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