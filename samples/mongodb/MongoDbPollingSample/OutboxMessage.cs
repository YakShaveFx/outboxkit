using MongoDB.Bson;
using YakShaveFx.OutboxKit.Core;

namespace MongoDbPollingSample;

public sealed class OutboxMessage : IMessage
{
    public ObjectId Id { get; init; }
    public required string Type { get; init; }
    public required byte[] Payload { get; init; }
    public DateTime CreatedAt { get; init; }
    public byte[]? TraceContext { get; init; }
    public DateTime? ProcessedAt { get; init; }
}