using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace YakShaveFx.OutboxKit.Core.OpenTelemetry;

public static class OutboxKitInstrumentationTracerProviderBuilderExtensions
{
    /// <summary>
    /// Sets up OutboxKit OpenTelemetry tracing instrumentation.
    /// </summary>
    /// <param name="builder">The <see cref="TracerProviderBuilder"/> to configure.</param>
    /// <returns>The supplied <see cref="TracerProviderBuilder"/> for chaining calls.</returns>
    public static TracerProviderBuilder AddOutboxKitInstrumentation(this TracerProviderBuilder builder)
        => builder.AddSource(ActivityHelpers.ActivitySource.Name);
}

public static class OutboxKitInstrumentationMeterProviderBuilderExtensions
{
    /// <summary>
    /// Sets up OutboxKit OpenTelemetry metrics instrumentation.
    /// </summary>
    /// <param name="builder">The <see cref="MeterProviderBuilder"/> to configure.</param>
    /// <returns>The supplied <see cref="MeterProviderBuilder"/> for chaining calls.</returns>
    public static MeterProviderBuilder AddOutboxKitInstrumentation(this MeterProviderBuilder builder)
        => builder.AddMeter(MetricShared.MeterName);
}