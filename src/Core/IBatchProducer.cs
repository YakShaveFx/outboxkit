using Microsoft.Extensions.DependencyInjection;

namespace YakShaveFx.OutboxKit.Core;

public interface IBatchProducer
{
    Task<BatchProduceResult> ProduceAsync(string key, IReadOnlyCollection<IMessage> messages, CancellationToken ct);
}

public sealed class BatchProduceResult
{
    public required IReadOnlyCollection<IMessage> Ok { get; init; }
}

public interface IBatchProducerProvider
{
    IBatchProducer Get();
}

internal sealed class BatchProducerProvider(Type batchProducerType, IServiceProvider services) : IBatchProducerProvider
{
    public IBatchProducer Get() => (IBatchProducer)services.GetRequiredService(batchProducerType);
}