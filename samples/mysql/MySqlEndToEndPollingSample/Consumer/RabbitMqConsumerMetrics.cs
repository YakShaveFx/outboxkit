using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MySqlEndToEndPollingSample.Consumer;

internal sealed class RabbitMqConsumerMetrics : IDisposable
{
    public const string MeterName = "RabbitMqConsumer";
    
    private readonly Meter _meter;
    private readonly Counter<long> _consumedMessagesCounter;
    
    public RabbitMqConsumerMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);
        
        _consumedMessagesCounter = _meter.CreateCounter<long>(
            "rabbitmq.consumer.consumed_messages",
            unit: "{message}",
            description: "The number of message consumed");
        
    }

    public void MessageConsumed(string exchange)
    {
        if (_consumedMessagesCounter.Enabled)
        {
            var tags = new TagList { { "exchange", exchange } };
            _consumedMessagesCounter.Add(1, tags);
        }
    }
    
    public void Dispose() => _meter.Dispose();
}