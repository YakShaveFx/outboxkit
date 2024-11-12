using YakShaveFx.OutboxKit.Core;

namespace YakShaveFx.OutboxKit.MySql;

/// <summary>
/// Default implementation of <see cref="IMessage"/>.
/// </summary>
public sealed record Message : IMessage
{
    /// <summary>
    /// The message auto-incrementing identifier.
    /// </summary>
    public long Id { get; init; }
    
    /// <summary>
    /// The type of the payload stored in the message.
    /// </summary>
    public required string Type { get; init; }
    
    /// <summary>
    /// The message payload.
    /// </summary>
    public required byte[] Payload { get; init; }
    
    /// <summary>
    /// The date/time at which the message was created.
    /// </summary>
    public required DateTime CreatedAt { get; init; }
    
    /// <summary>
    /// The trace context captured at the time the message was created.
    /// </summary>
    public byte[]? TraceContext { get; init; }
}