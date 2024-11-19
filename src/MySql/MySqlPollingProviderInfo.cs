using YakShaveFx.OutboxKit.Core;

namespace YakShaveFx.OutboxKit.MySql;

/// <summary>
/// Exposes the provider information and helper functions for the MySQL polling provider.
/// </summary>
public static class MySqlPollingProviderInfo
{
    private const string ProviderKey = "mysql_polling";

    /// <summary>
    /// The provider identifier used in the <see cref="OutboxKey.ProviderKey"/> property of <see cref="OutboxKey"/>.
    /// </summary>
    public static string Key => ProviderKey;

    /// <summary>
    /// The default outbox key for the MySQL polling provider.
    /// </summary>
    public static OutboxKey DefaultKey { get; } = new(ProviderKey);

    /// <summary>
    /// Creates the outbox key for the MySQL polling provider with the specified key. 
    /// </summary>
    /// <param name="clientKey">The client key assigned to the outbox.</param>
    /// <returns>The outbox key for the MySQL polling provider with the specified client key.</returns>
    public static OutboxKey CreateKey(string clientKey) => new(ProviderKey, clientKey);
}