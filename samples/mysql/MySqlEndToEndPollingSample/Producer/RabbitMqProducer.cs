using System.Net.Mime;
using System.Text.Json;
using RabbitMQ.Client;
using YakShaveFx.OutboxKit.Core;
using YakShaveFx.OutboxKit.MySql;

using static YakShaveFx.OutboxKit.Core.OpenTelemetry.TraceContextHelpers;

namespace MySqlEndToEndPollingSample.Producer;

internal sealed class RabbitMqProducer : IBatchProducer
{
    private readonly IModel _model;
    private readonly RabbitMqSettings _settings;
    private readonly ILogger<RabbitMqProducer> _logger;
    private readonly RabbitMqProducerMetrics _metrics;


    public RabbitMqProducer(
        IConnection connection,
        RabbitMqSettings settings,
        ILogger<RabbitMqProducer> logger,
        RabbitMqProducerMetrics metrics)
    {
        _settings = settings;
        _logger = logger;
        _metrics = metrics;
        _model = connection.CreateModel();
        _model.ExchangeDeclare(settings.Exchange, ExchangeType.Topic);
    }

    public Task<BatchProduceResult> ProduceAsync(OutboxKey key, IReadOnlyCollection<IMessage> messages, CancellationToken ct)
    {
        var ok = new List<IMessage>();
        foreach (var message in messages.Cast<Message>())
        {
            try
            {
                var parentContext = RestoreTraceContext(message.TraceContext);
                using var activity = RabbitMqProducerActivitySource.StartActivity(
                    parentContext,
                    _settings.Exchange,
                    JsonSerializer.Deserialize<SampleEvent>(message.Payload)!);
                var properties = _model.CreateBasicProperties();
                properties.ContentType = MediaTypeNames.Application.Json;
                properties.DeliveryMode = 2;
                properties.Headers = RabbitMqProducerActivitySource.EnrichHeadersWithTracingContext(activity, []);
                _model.BasicPublish(_settings.Exchange, nameof(SampleEvent), properties, message.Payload);
                ok.Add(message);
                _metrics.MessagePublished(_settings.Exchange);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to produce message with id \"{Id}\", aborting", message.Id);
                break;
            }
        }

        return Task.FromResult(new BatchProduceResult { Ok = ok });
    }
}