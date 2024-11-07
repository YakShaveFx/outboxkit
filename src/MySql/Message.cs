using YakShaveFx.OutboxKit.Core;

namespace YakShaveFx.OutboxKit.MySql;

public sealed record Message : IMessage
{
    public long Id { get; init; }
    public required string Type { get; init; }
    public required byte[] Payload { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public byte[]? ObservabilityContext { get; init; }
}