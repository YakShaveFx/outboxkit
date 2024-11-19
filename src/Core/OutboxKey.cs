namespace YakShaveFx.OutboxKit.Core;

/// <summary>
/// Represents an outbox key, which is a combination of a provider (e.g. MySQL polling) and a key (e.g. a tenant identifier).
/// </summary>
/// <param name="ProviderKey">Identifies the provider responsible for this outbox.</param>
/// <param name="ClientKey">Identifies this outbox from a client's perspective.</param>
public readonly record struct OutboxKey(string ProviderKey, string ClientKey = "default")
{
    /// <inheritdoc />
    public override string ToString() => $"{ProviderKey}:{ClientKey}";
}