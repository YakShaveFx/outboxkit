using YakShaveFx.OutboxKit.MySql.Polling;
using YakShaveFx.OutboxKit.MySql.Shared;

namespace YakShaveFx.OutboxKit.MySql.Tests.Polling;

internal static class BatchFetcherTestHelpers
{
    public record Config(
        DefaultSchemaSettings DefaultSchemaSettings,
        MySqlPollingSettings MySqlPollingSettings,
        TableConfiguration TableConfig);

    public static Config GetConfigs(CompletionMode completionMode) =>
        completionMode switch
        {
            CompletionMode.Delete => new Config(
                Defaults.Delete.DefaultSchemaSettings,
                Defaults.Delete.MySqlPollingSettings,
                Defaults.Delete.TableConfig
            ),
            CompletionMode.Update => new Config(
                Defaults.Update.DefaultSchemaSettings,
                Defaults.Update.MySqlPollingSettings,
                Defaults.Update.TableConfigWithProcessedAt
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(completionMode))
        };
}