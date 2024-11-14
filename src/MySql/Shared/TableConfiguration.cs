using MySqlConnector;
using YakShaveFx.OutboxKit.Core;

namespace YakShaveFx.OutboxKit.MySql.Shared;

internal sealed record TableConfiguration(
    string Name,
    IReadOnlyCollection<string> Columns,
    string IdColumn,
    string OrderByColumn,
    string ProcessedAtColumn,
    Func<IMessage, object> IdGetter,
    Func<MySqlDataReader, IMessage> MessageFactory)
{
    public static TableConfiguration Default { get; } = new(
        "outbox_messages",
        [
            "id",
            "type",
            "payload",
            "created_at",
            "trace_context"
        ],
        "id",
        "id",
        "",
        m => ((Message)m).Id,
        r => new Message
        {
            Id = r.GetInt64(0),
            Type = r.GetString(1),
            Payload = r.GetFieldValue<byte[]>(2),
            CreatedAt = r.GetDateTime(3),
            TraceContext = r.IsDBNull(4) ? null : r.GetFieldValue<byte[]>(4)
        });
}