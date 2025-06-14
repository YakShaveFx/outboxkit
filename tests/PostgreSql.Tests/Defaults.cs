using YakShaveFx.OutboxKit.Core.Polling;
using YakShaveFx.OutboxKit.PostgreSql.Polling;
using YakShaveFx.OutboxKit.PostgreSql.Shared;

namespace YakShaveFx.OutboxKit.PostgreSql.Tests;

internal static class Defaults
{
    internal static class Delete
    {
        internal static DefaultSchemaSettings DefaultSchemaSettings = new()
        {
            WithProcessedAtColumn = false
        };
        
        internal static readonly CorePollingSettings CorePollingSettings = new()
        {
            EnableCleanUp = false
        };

        internal static readonly PostgreSqlPollingSettings PostgreSqlPollingSettings = new()
        {
            BatchSize = 5
        };

        internal static readonly TableConfiguration TableConfig = TableConfiguration.Default;
    }

    internal static class Update
    {
        internal static DefaultSchemaSettings DefaultSchemaSettings = Delete.DefaultSchemaSettings with
        {
            WithProcessedAtColumn = true
        };
        
        internal static readonly CorePollingSettings CorePollingSettings = Delete.CorePollingSettings with
        {
            EnableCleanUp = true
        };

        internal static readonly PostgreSqlPollingSettings PostgreSqlPollingSettings = Delete.PostgreSqlPollingSettings with
        {
            CompletionMode = CompletionMode.Update
        };


        internal static readonly TableConfiguration TableConfigWithProcessedAt = Delete.TableConfig with
        {
            ProcessedAtColumn = "processed_at"
        };
    }
}