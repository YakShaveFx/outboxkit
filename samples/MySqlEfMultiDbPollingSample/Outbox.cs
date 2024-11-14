using System.Text;
using YakShaveFx.OutboxKit.Core;
using YakShaveFx.OutboxKit.MySql;

namespace MySqlEfMultiDbPollingSample;

internal sealed class FakeBatchProducer(ILogger<FakeBatchProducer> logger) : IBatchProducer
{
    public Task<BatchProduceResult> ProduceAsync(OutboxKey key, IReadOnlyCollection<IMessage> messages, CancellationToken ct)
    {
        var x = messages.Cast<Message>().ToList();
        logger.LogInformation("Producing {Count} messages", x.Count);
        foreach (var message in x)
        {
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

        return Task.FromResult(new BatchProduceResult { Ok = x });
    }
}