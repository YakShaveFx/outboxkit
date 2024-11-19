using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace YakShaveFx.OutboxKit.Core.OpenTelemetry;

internal sealed class CleanerMetrics : IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _producedMessagesCounter;

    public CleanerMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(Constants.MeterName);
        
        _producedMessagesCounter = _meter.CreateCounter<long>(
            "outbox.cleaned_messages",
            unit: "{message}",
            description: "The number processed outbox messages cleaned");
    }
    
    public void MessagesCleaned(OutboxKey key, int count)
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