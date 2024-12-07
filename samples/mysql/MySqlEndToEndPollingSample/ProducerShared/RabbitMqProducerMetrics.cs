using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MySqlEndToEndPollingSample.ProducerShared;

public class RabbitMqProducerMetrics : IDisposable
{
    public const string MeterName = "RabbitMqProducer";
    
    private readonly Meter _meter;
    private readonly Counter<long> _producedMessagesCounter;
    
    public RabbitMqProducerMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);
        
        _producedMessagesCounter = _meter.CreateCounter<long>(
            "rabbitmq.producer.produced_messages",
            unit: "{message}",
            description: "The number of message produced");
        
    }

    public void MessagePublished(string exchange)
    {
        if (_producedMessagesCounter.Enabled)
        {
            var tags = new TagList { { "exchange", exchange } };
            _producedMessagesCounter.Add(1, tags);
        }
    }
    
    public void Dispose() => _meter.Dispose();
}