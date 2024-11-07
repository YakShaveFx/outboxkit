using YakShaveFx.OutboxKit.MySql.Polling;

namespace YakShaveFx.OutboxKit.MySql.Tests.Polling;

internal static class Defaults
{
    internal static readonly MySqlPollingSettings DefaultPollingSettings = new()
    {
        BatchSize = 5
    };

    internal static readonly TableConfiguration DefaultTableConfig = TableConfiguration.Default;
}