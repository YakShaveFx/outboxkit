using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace YakShaveFx.OutboxKit.Core.OpenTelemetry;

/// <summary>
/// Provides some helper functions to aid in implementing tracing.
/// </summary>
public static class TraceContextHelpers
{
    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

    /// <summary>
    /// Extracts the current trace context, from the current activity, if any.
    /// </summary>
    /// <returns>A <see cref="T:byte[]"/> containing the current trace context serialized, <code>null</code> if no context is available.</returns>
    /// <remarks>Context can later be restored by using <see cref="RestoreTraceContext"/>.</remarks>
    public static byte[]? ExtractCurrentTraceContext()
    {
        var activity = Activity.Current;

        if (activity is null) return null;

        var extractedContext = new List<ContextEntry>(1);

        Propagator.Inject(new (activity.Context, Baggage.Current), extractedContext, InjectEntry);

        return JsonSerializer.SerializeToUtf8Bytes(extractedContext, SourceGenerationContext.Default.ListContextEntry);

        static void InjectEntry(List<ContextEntry> context, string key, string value) => context.Add(new(key, value));
    }

    /// <summary>
    /// Restores the trace context from a serialized representation.
    /// </summary>
    /// <param name="traceContext">The serialized trace context, if any.</param>
    /// <returns>A <see cref="PropagationContext"/> </returns>
    /// <remarks>Restores the context extracted by <see cref="ExtractCurrentTraceContext"/>.</remarks>
    public static PropagationContext? RestoreTraceContext(byte[]? traceContext)
    {
        if (traceContext is not { Length: > 0 }) return null;

        var deserializedContext = JsonSerializer.Deserialize(
            traceContext,
            SourceGenerationContext.Default.ListContextEntry);
        
        if (deserializedContext is null) return null;

        return Propagator.Extract(default, deserializedContext, ExtractEntry);

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