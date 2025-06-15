using System.Text;
using YakShaveFx.OutboxKit.Core;
using YakShaveFx.OutboxKit.PostgreSql;

namespace PostgreSqlEfMultiDbPollingSample;

internal sealed class FakeBatchProducer(ILogger<FakeBatchProducer> logger) : IBatchProducer
{
    public Task<BatchProduceResult> ProduceAsync(OutboxKey key, IReadOnlyCollection<IMessage> messages, CancellationToken ct)
    {
        logger.LogInformation("Producing {Count} messages", messages.Count);
        foreach (var message in messages.Cast<Message>())
        {
            logger.LogInformation(
                """key {Key}, id {Id}, type {Type}, payload "{Payload}", created_at {CreatedAt}, trace_context {TraceContext}""",
                key,
                message.Id,
                message.Type,
                Encoding.UTF8.GetString(message.Payload),
                message.CreatedAt,
                message.TraceContext is null ? "null" : $"{message.TraceContext.Length} bytes");
        }

        return Task.FromResult(new BatchProduceResult { Ok = messages });
    }
}