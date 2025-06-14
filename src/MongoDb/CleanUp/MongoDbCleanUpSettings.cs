using System.Linq.Expressions;
using YakShaveFx.OutboxKit.Core;

namespace YakShaveFx.OutboxKit.MongoDb.CleanUp;

internal sealed record MongoDbCleanUpSettings
{
    public TimeSpan MaxAge { get; init; } = TimeSpan.FromDays(1);
}

internal sealed record MongoDbCleanUpCollectionSettings<TMessage> where TMessage : IMessage
{
    public required string Name { get; init; } 
    public required Expression<Func<TMessage, DateTime?>> ProcessedAtSelector { get; init; }
}
