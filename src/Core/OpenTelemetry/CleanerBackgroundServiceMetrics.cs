using System.Diagnostics;
using System.Diagnostics.Metrics;
using static YakShaveFx.OutboxKit.Core.OpenTelemetry.MetricsConstants.CleanUpBackgroundService;
using static YakShaveFx.OutboxKit.Core.OpenTelemetry.MetricsConstants;

namespace YakShaveFx.OutboxKit.Core.OpenTelemetry;

internal sealed class CleanerBackgroundServiceMetrics : IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _cleanedMessagesCounter;

    public CleanerBackgroundServiceMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(Constants.MeterName);
        
        _cleanedMessagesCounter = _meter.CreateCounter<long>(
            CleanedMessages.Name,
            CleanedMessages.Unit,
            CleanedMessages.Description);
    }
    
    public void MessagesCleaned(OutboxKey key, int count)
    {
        if (_cleanedMessagesCounter.Enabled && count > 0)
        {
            var tags = new TagList
            {
                { Shared.Tags.ProviderKeyTag, key.ProviderKey },
                { Shared.Tags.ClientKeyTag, key.ClientKey }
            };
            _cleanedMessagesCounter.Add(count, tags);
        }
    }

    public void Dispose() => _meter.Dispose();
}