using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace MySqlEndToEndPollingSample.Consumer;

internal sealed class RabbitMqConsumer(
    IConnection connection,
    RabbitMqSettings settings,
    ILogger<RabbitMqConsumer> logger,
    RabbitMqConsumerMetrics metrics) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var model = connection.CreateModel();
            model.ExchangeDeclare(settings.Exchange, ExchangeType.Topic);
            model.QueueDeclare(settings.Queue, true, false, false, null);
            model.QueueBind(settings.Queue, settings.Exchange, "*");
            var consumer = new EventingBasicConsumer(model);
            consumer.Received += OnReceived;
            var consumerTag = model.BasicConsume(settings.Queue, true, consumer);

            var tcs = new TaskCompletionSource();
            await using var _ = stoppingToken.Register(() => tcs.SetResult());
            await tcs.Task;

            model.BasicCancel(consumerTag);
        }
    }

    void OnReceived(object? sender, BasicDeliverEventArgs eventArgs)
    {
        var @event = JsonSerializer.Deserialize<SampleEvent>(eventArgs.Body.Span)!;
        using var activity = RabbitMqProducerActivitySource.StartActivity(
            settings.Exchange,
            @event,
            eventArgs.BasicProperties.Headers);

        logger.LogInformation("Consumed message: {Event}", JsonSerializer.Serialize(@event));
        metrics.MessageConsumed(settings.Exchange);
    }
}