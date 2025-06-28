using System.Diagnostics;

namespace YakShaveFx.OutboxKit.Core.OpenTelemetry;

internal static class ActivityHelpers
{
    internal static readonly ActivitySource ActivitySource = new(
        Constants.ActivitySourceName,
        typeof(ActivityHelpers).Assembly.GetName().Version!.ToString());

    public static Activity? StartActivity(string activityName, OutboxKey key) 
        => StartActivity(activityName, key, []);

    public static Activity? StartActivity(
        string activityName,
        OutboxKey key,
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        if (!ActivitySource.HasListeners())
        {
            return null;
        }

        // ReSharper disable once ExplicitCallerInfoArgument
        return ActivitySource.StartActivity(
            name: activityName,
            kind: ActivityKind.Internal,
            tags:
            [
                new(ActivityConstants.OutboxProviderKeyTag, key.ProviderKey),
                new(ActivityConstants.OutboxClientKeyTag, key.ClientKey),
                ..tags
            ]);
    }
}

internal static class ActivityConstants
{
    public const string OutboxProviderKeyTag = "outbox.provider_key";
    public const string OutboxClientKeyTag = "outbox.client_key";
    public const string OutboxBatchSizeTag = "outbox.batch.size";
    public const string OutboxCleanedCountTag = "outbox.cleaned.count";
    public const string OutboxProducedMessagesToCompleteTag = "outbox.produced_messages_to_complete";
}