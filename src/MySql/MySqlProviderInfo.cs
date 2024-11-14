using YakShaveFx.OutboxKit.Core;

namespace YakShaveFx.OutboxKit.MySql;

public static class MySqlProviderInfo
{
    private const string PollingKey = "mysql_polling";

    /// <summary>
    /// The provider identifier used in the <see cref="OutboxKey.ProviderKey"/> property of <see cref="OutboxKey"/>.
    /// </summary>
    public static string PollingProvider => PollingKey;

    /// <summary>
    /// Creates the provider key for the MySQL polling provider with the specified key. 
    /// </summary>
    /// <param name="key">The base key assigned to the outbox.</param>
    /// <returns></returns>
    public static OutboxKey CreatePollingKey(string key) => new(PollingKey, key);
}