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

        // not following the convention of using snake_case, so that we test that the provider
        // quotes things correctly and everything works as expected
        internal static readonly TableConfiguration TableConfig =  new(
            "OutboxMessages",
            [
                "Id",
                "Type",
                "Payload",
                "CreatedAt",
                "TraceContext"
            ],
            "Id",
            [new("Id")],
            "",
            m => ((Message)m).Id,
            static r => new Message
            {
                Id = r.GetInt64(0),
                Type = r.GetString(1),
                Payload = r.GetFieldValue<byte[]>(2),
                CreatedAt = r.GetDateTime(3),
                TraceContext = r.IsDBNull(4) ? null : r.GetFieldValue<byte[]>(4)
            });
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
            ProcessedAtColumn = "ProcessedAt",
        };
    }
}