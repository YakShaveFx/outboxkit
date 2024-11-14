using System.Diagnostics;
using System.Text;
using YakShaveFx.OutboxKit.Core;
using static YakShaveFx.OutboxKit.Core.OpenTelemetry.TraceContextHelpers;

namespace MySqlEfPollingSample;

internal sealed class FakeBatchProducer(ILogger<FakeBatchProducer> logger) : IBatchProducer
{
    public static ActivitySource ActivitySource { get; } = new(typeof(FakeBatchProducer).Assembly.GetName().Name!);
    
    public Task<BatchProduceResult> ProduceAsync(OutboxKey key, IReadOnlyCollection<IMessage> messages, CancellationToken ct)
    {
        var x = messages.Cast<OutboxMessage>().ToList();
        logger.LogInformation("Producing {Count} messages", x.Count);
        foreach (var message in x)
        {
            using var activity = StartActivityFromTraceContext(message.TraceContext);
            
            logger.LogInformation(
                """provider {provider}, key {Key}, id {Id}, type {Type}, payload "{Payload}", created_at {CreatedAt}, trace_context {TraceContext}""",
                key.ProviderKey,
                key.ClientKey,
                message.Id, 
                message.Type,
                Encoding.UTF8.GetString(message.Payload),
                message.CreatedAt,
                message.TraceContext is null ? "null" : $"{message.TraceContext.Length} bytes");
        }

        return Task.FromResult(new BatchProduceResult{Ok = x});
    }
    
    private static Activity? StartActivityFromTraceContext(byte[]? traceContext)
    {
        var parentContext = RestoreTraceContext(traceContext);

        return ActivitySource.StartActivity(
            "produce message",
            ActivityKind.Producer,
            parentContext: parentContext?.ActivityContext ?? default);
    }
}