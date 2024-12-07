using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace MySqlEndToEndPollingSample.ProducerShared;

// note: new versions of RabbitMQ client library will eventually support OpenTelemetry out of the box,
// but this version still needs manual instrumentation 

public static class RabbitMqProducerActivitySource
{
    public const string ActivitySourceName = nameof(RabbitMqProducer);

    private const string Name = "message produce";
    private const ActivityKind Kind = ActivityKind.Producer;
    private const string MessageExchangeTag = "message.exchange";
    private const string MessageIdTag = "message.id";
    private const string MessageTypeTag = "message.type";

    private static readonly ActivitySource ActivitySource
        = new(ActivitySourceName);

    private static readonly TextMapPropagator Propagator
        = Propagators.DefaultTextMapPropagator;

    public static Activity? StartActivity(PropagationContext? parentContext, string exchange, SampleEvent sampleEvent)
    {
        if (!ActivitySource.HasListeners())
        {
            return null;
        }

        var links = Activity.Current is { } currentActivity
            ? new[] { new ActivityLink(currentActivity.Context) }
            : default;

        // ReSharper disable once ExplicitCallerInfoArgument
        return ActivitySource.StartActivity(
            Name,
            kind: Kind,
            parentContext: parentContext?.ActivityContext ?? default,
            links: links,
            tags:
            [
                new(MessageExchangeTag, exchange),
                new(MessageIdTag, sampleEvent.Id),
                new(MessageTypeTag, sampleEvent.GetType().Name)
            ]);
    }

    public static Dictionary<string, object> EnrichHeadersWithTracingContext(
        Activity? activity,
        Dictionary<string, object> headers)
    {
        if (activity is null)
        {
            return headers;
        }

        // on the receiving side,
        // the service will extract this information
        // to maintain the overall tracing context

        var contextToInject = activity?.Context
                              ?? Activity.Current?.Context
                              ?? default;

        Propagator.Inject(
            new PropagationContext(contextToInject, Baggage.Current),
            headers,
            InjectTraceContext);

        return headers;

        static void InjectTraceContext(Dictionary<string, object> headers, string key, string value)
            => headers.Add(key, value);
    }
}