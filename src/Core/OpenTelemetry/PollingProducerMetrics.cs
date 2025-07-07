using System.Diagnostics;
using System.Diagnostics.Metrics;
using static YakShaveFx.OutboxKit.Core.OpenTelemetry.MetricsConstants.PollingProducer;
using static YakShaveFx.OutboxKit.Core.OpenTelemetry.MetricsConstants;

namespace YakShaveFx.OutboxKit.Core.OpenTelemetry;

internal sealed class PollingProducerMetrics : IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _producedBatchesCounter;
    private readonly Counter<long> _producedMessagesCounter;

    public PollingProducerMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(Constants.MeterName);
        
        _producedBatchesCounter = _meter.CreateCounter<long>(
            ProducedBatches.Name,
            ProducedBatches.Unit,
            ProducedBatches.Description);
        
        _producedMessagesCounter = _meter.CreateCounter<long>(
            ProducedMessages.Name,
            ProducedMessages.Unit,
            ProducedMessages.Description);
    }
    
    public void BatchProduced(OutboxKey key, bool allMessagesProduced)
    {
        if (_producedBatchesCounter.Enabled)
        {
            var tags = new TagList
            {
                { Shared.Tags.ProviderKeyTag, key.ProviderKey },
                { Shared.Tags.ClientKeyTag, key.ClientKey },
                { ProducedBatches.Tags.AllMessagesProduced, allMessagesProduced }
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
                { Shared.Tags.ProviderKeyTag, key.ProviderKey },
                { Shared.Tags.ClientKeyTag, key.ClientKey }
            };
            _producedMessagesCounter.Add(count, tags);
        }
    }

    public void Dispose() => _meter.Dispose();
}