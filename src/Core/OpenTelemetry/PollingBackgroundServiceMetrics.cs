using System.Diagnostics;
using System.Diagnostics.Metrics;
using YakShaveFx.OutboxKit.Core.Polling;
using static YakShaveFx.OutboxKit.Core.OpenTelemetry.MetricsConstants.PollingBackgroundService;
using static YakShaveFx.OutboxKit.Core.OpenTelemetry.MetricsConstants;

namespace YakShaveFx.OutboxKit.Core.OpenTelemetry;

internal sealed class PollingBackgroundServiceMetrics : IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _pollingCyclesCounter;

    public PollingBackgroundServiceMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(Constants.MeterName);
        
        _pollingCyclesCounter = _meter.CreateCounter<long>(
            PollingCycles.Name,
            PollingCycles.Unit,
            PollingCycles.Description);
    }
    
    public void PollingCycleExecuted(OutboxKey key, ProducePendingResult result)
    {
        if (_pollingCyclesCounter.Enabled)
        {
            var tags = new TagList
            {
                { Shared.Tags.ProviderKeyTag, key.ProviderKey },
                { Shared.Tags.ClientKeyTag, key.ClientKey },
                { PollingCycles.Tags.Result, GetResultTagValue(result) }
            };
            _pollingCyclesCounter.Add(1, tags);
        }
    }
    
    private static string GetResultTagValue(ProducePendingResult result)
    {
        return result switch
        {
            ProducePendingResult.Ok => nameof(ProducePendingResult.Ok),
            ProducePendingResult.FetchError => nameof(ProducePendingResult.FetchError),
            ProducePendingResult.ProduceError => nameof(ProducePendingResult.ProduceError),
            ProducePendingResult.PartialProduction => nameof(ProducePendingResult.PartialProduction),
            ProducePendingResult.CompleteError => nameof(ProducePendingResult.CompleteError),
            _ => ThrowInvalidResultException(result)
        };

        static string ThrowInvalidResultException(ProducePendingResult result) 
            => throw new ArgumentOutOfRangeException(nameof(result), result, $"Unknown value {result}");
    }


    public void Dispose() => _meter.Dispose();
}