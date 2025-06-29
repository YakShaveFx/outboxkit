using System.Diagnostics;
using System.Diagnostics.Metrics;
using static YakShaveFx.OutboxKit.Core.OpenTelemetry.MetricsConstants.CompletionRetrier;
using static YakShaveFx.OutboxKit.Core.OpenTelemetry.MetricsConstants;

namespace YakShaveFx.OutboxKit.Core.OpenTelemetry;

internal sealed class CompletionRetrierMetrics : IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _completionRetryAttemptsCounter;
    private readonly Counter<long> _completionRetriedMessagesCounter;
    private readonly UpDownCounter<int> _pendingRetryCounter;
    
    public CompletionRetrierMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(Constants.MeterName);
        
        _completionRetryAttemptsCounter = _meter.CreateCounter<long>(
            CompletionRetryAttempts.Name,
            CompletionRetryAttempts.Unit,
            CompletionRetryAttempts.Description);
        
        _completionRetriedMessagesCounter = _meter.CreateCounter<long>(
            CompletionRetriedMessages.Name, 
            CompletionRetriedMessages.Unit, 
            CompletionRetriedMessages.Description);
        
        _pendingRetryCounter = _meter.CreateUpDownCounter<int>(
            PendingRetry.Name,
            PendingRetry.Unit,
            PendingRetry.Description);
    }
    
    public void CompletionRetryAttempted(OutboxKey key, int count)
    {
        if (_completionRetryAttemptsCounter.Enabled && count > 0)
        {
            var tags = new TagList
            {
                { Shared.Tags.ProviderKeyTag, key.ProviderKey },
                { Shared.Tags.ClientKeyTag, key.ClientKey }
            };
            _completionRetryAttemptsCounter.Add(1, tags);
        }
        
        if (_completionRetriedMessagesCounter.Enabled && count > 0)
        {
            var tags = new TagList
            {
                { Shared.Tags.ProviderKeyTag, key.ProviderKey },
                { Shared.Tags.ClientKeyTag, key.ClientKey }
            };
            _completionRetriedMessagesCounter.Add(count, tags);
        }
    }
    
    public void NewMessagesPendingRetry(OutboxKey key, int count)
    {
        if (_pendingRetryCounter.Enabled)
        {
            var tags = new TagList
            {
                { Shared.Tags.ProviderKeyTag, key.ProviderKey },
                { Shared.Tags.ClientKeyTag, key.ClientKey }
            };
            _pendingRetryCounter.Add(count, tags);
        }
    }
    
    public void MessagesCompleted(OutboxKey key, int count)
    {
        if (_pendingRetryCounter.Enabled && count > 0)
        {
            var tags = new TagList
            {
                { Shared.Tags.ProviderKeyTag, key.ProviderKey },
                { Shared.Tags.ClientKeyTag, key.ClientKey }
            };
            _pendingRetryCounter.Add(-count, tags);
        }
    }
    
    public void Dispose() => _meter.Dispose();
}