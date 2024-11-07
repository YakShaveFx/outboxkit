using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace YakShaveFx.OutboxKit.Core.OpenTelemetry;

public static class ObservabilityContextHelpers
{
    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

    public static byte[]? GetCurrentObservabilityContext()
    {
        var activity = Activity.Current;

        if (activity is null)
        {
            return null;
        }

        var extractedContext = new List<ContextEntry>(1);

        Propagator.Inject(
            new PropagationContext(activity.Context, Baggage.Current),
            extractedContext,
            InjectEntry);

        return JsonSerializer.SerializeToUtf8Bytes(extractedContext, SourceGenerationContext.Default.ListContextEntry);

        static void InjectEntry(List<ContextEntry> context, string key, string value)
            => context.Add(new(key, value));
    }

    public static PropagationContext? ExtractParentContext(byte[]? observabilityContext)
    {
        if (observabilityContext is not { Length: > 0 })
        {
            return null;
        }

        var deserializedContext = JsonSerializer.Deserialize<List<ContextEntry>>(observabilityContext)!;

        return Propagator.Extract(
            default,
            deserializedContext,
            ExtractEntry);

        static IEnumerable<string> ExtractEntry(List<ContextEntry> context, string key)
        {
            foreach (var entry in context)
            {
                if (entry.Key == key)
                {
                    yield return entry.Value;
                }
            }
        }
    }
}

internal record struct ContextEntry(string Key, string Value);

[JsonSerializable(typeof(List<ContextEntry>))]
internal partial class SourceGenerationContext : JsonSerializerContext;