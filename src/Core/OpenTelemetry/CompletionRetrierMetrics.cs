using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace YakShaveFx.OutboxKit.Core.OpenTelemetry;

internal sealed class CompletionRetrierMetrics : IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _completionRetryAttemptsCounter;
    private readonly Counter<long> _completionRetriedMessagesCounter;
    
    public CompletionRetrierMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(Constants.MeterName);
        
        _completionRetryAttemptsCounter = _meter.CreateCounter<long>(
            "outbox.completion_retry_attempts",
            unit: "{attempt}",
            description: "The number of attempts to retry completion of produced messages");
        
        _completionRetriedMessagesCounter = _meter.CreateCounter<long>(
            "outbox.completion_retried_messages",
            unit: "{message}",
            description: "The number of messages for which completion was retried");
            
    }
    
    public void CompletionRetryAttempted(OutboxKey key, int count)
    {
        if (_completionRetryAttemptsCounter.Enabled && count > 0)
        {
            var tags = new TagList
            {
                { "provider_key", key.ProviderKey },
                { "client_key", key.ClientKey }
            };
            _completionRetryAttemptsCounter.Add(1, tags);
        }
        
        if (_completionRetriedMessagesCounter.Enabled && count > 0)
        {
            var tags = new TagList
            {
                { "provider_key", key.ProviderKey },
                { "client_key", key.ClientKey }
            };
            _completionRetriedMessagesCounter.Add(count, tags);
        }
    }
    
    public void Dispose() => _meter.Dispose();
}