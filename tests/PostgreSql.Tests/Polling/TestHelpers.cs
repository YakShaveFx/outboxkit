using YakShaveFx.OutboxKit.PostgreSql.Polling;
using YakShaveFx.OutboxKit.PostgreSql.Shared;

namespace YakShaveFx.OutboxKit.PostgreSql.Tests.Polling;

internal static class TestHelpers
{
    public record Config(
        DefaultSchemaSettings DefaultSchemaSettings,
        PostgreSqlPollingSettings PostgreSqlPollingSettings,
        TableConfiguration TableConfig);

    public static Config GetConfigs(CompletionMode completionMode) =>
        completionMode switch
        {
            CompletionMode.Delete => new Config(
                Defaults.Delete.DefaultSchemaSettings,
                Defaults.Delete.PostgreSqlPollingSettings,
                Defaults.Delete.TableConfig
            ),
            CompletionMode.Update => new Config(
                Defaults.Update.DefaultSchemaSettings,
                Defaults.Update.PostgreSqlPollingSettings,
                Defaults.Update.TableConfigWithProcessedAt
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(completionMode))
        };
}