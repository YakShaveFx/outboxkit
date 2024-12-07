using System.Diagnostics;
using System.Text;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace MySqlEndToEndPollingSample.Consumer;

internal static class RabbitMqProducerActivitySource
{
    public const string ActivitySourceName = nameof(RabbitMqConsumer);
    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private const string Name = "message consume";
    private const ActivityKind Kind = ActivityKind.Consumer;
    private const string MessageExchangeTag = "message.exchange";
    private const string MessageIdTag = "message.id";
    private const string MessageTypeTag = "message.type";

    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

    public static Activity? StartActivity(
        string exchange,
        SampleEvent @event,
        IDictionary<string, object>? headers)
    {
        if (!ActivitySource.HasListeners())
        {
            return null;
        }

        // Extract the context injected in the headers by the publisher

        var parentContext = Propagator.Extract(
            default,
            headers,
            ExtractTraceContext);

        // Inject extracted info into current context
        Baggage.Current = parentContext.Baggage;

        return ActivitySource.StartActivity(
            Name,
            Kind,
            parentContext.ActivityContext,
            tags:
            [
                new(MessageExchangeTag, exchange),
                new(MessageIdTag, @event.Id),
                new(MessageTypeTag, @event.GetType().Name)
            ]);

        static IEnumerable<string> ExtractTraceContext(
            IDictionary<string, object>? headers,
            string key)
        {
            if (headers is not null && headers.TryGetValue(key, out var value))
            {
                return [Encoding.UTF8.GetString((byte[])value)];
            }

            return [];
        }
    }
}