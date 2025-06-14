using MongoDB.Bson;
using YakShaveFx.OutboxKit.Core;

namespace YakShaveFx.OutboxKit.MongoDb.Tests;

public record TestMessage : IMessage
{
    public ObjectId Id { get; init; }
    public required string Type { get; init; }
    public required byte[] Payload { get; init; }
    public required DateTime CreatedAt { get; init; }
    public byte[]? TraceContext { get; init; }
}

public sealed record TestMessageWithProcessedAt : TestMessage
{
    public required DateTime? ProcessedAt { get; init; }
}