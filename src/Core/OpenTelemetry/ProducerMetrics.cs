using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace YakShaveFx.OutboxKit.Core.OpenTelemetry;

internal sealed class ProducerMetrics : IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _producedBatchesCounter;
    private readonly Counter<long> _producedMessagesCounter;

    public ProducerMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(Constants.MeterName);
        
        _producedBatchesCounter = _meter.CreateCounter<long>(
            "outbox.produced_batches",
            unit: "{batch}",
            description: "The number of batches produced");
        
        _producedMessagesCounter = _meter.CreateCounter<long>(
            "outbox.produced_messages",
            unit: "{message}",
            description: "The number of messages produced");
    }
    
    public void BatchProduced(OutboxKey key, bool allMessagesProduced)
    {
        if (_producedBatchesCounter.Enabled)
        {
            var tags = new TagList
            {
                { "provider_key", key.ProviderKey },
                { "client_key", key.ClientKey },
                { "all_messages_produced", allMessagesProduced }
            };
            _producedBatchesCounter.Add(1, tags);
        }
    }
    
    public void MessagesProduced(OutboxKey key, int count)
    {
        if (_producedMessagesCounter.Enabled && count > 0)
        {
            var tags = new TagList
            {
                { "provider_key", key.ProviderKey },
                { "client_key", key.ClientKey }
            };
            _producedMessagesCounter.Add(count, tags);
        }
    }

    public void Dispose() => _meter.Dispose();
}