using MySqlConnector;
using YakShaveFx.OutboxKit.Core;

namespace YakShaveFx.OutboxKit.MySql.Shared;

/// <summary>
/// Defines the sort direction.
/// </summary>
public enum SortDirection
{
    /// <summary>
    /// Ascending sort direction.
    /// </summary>
    Ascending,
    /// <summary>
    /// Descending sort direction.
    /// </summary>
    Descending
}

/// <summary>
/// Defines the column and direction to sort the fetched outbox messages by.
/// </summary>
/// <param name="Column">The column to sort by.</param>
/// <param name="Direction">The direction in which to sort by.</param>
public record SortExpression(string Column, SortDirection Direction = SortDirection.Ascending);

internal sealed record TableConfiguration(
    string Name,
    IReadOnlyCollection<string> Columns,
    string IdColumn,
    IReadOnlyCollection<SortExpression> SortExpressions,
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
        [new("id")],
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