using System.Diagnostics;

namespace YakShaveFx.OutboxKit.Core.OpenTelemetry;

internal static class ActivityHelpers
{
    internal static readonly ActivitySource ActivitySource = new(
        Constants.ActivitySourceName,
        typeof(ActivityHelpers).Assembly.GetName().Version!.ToString());
    
    public static Activity? StartActivity(string activityName, string key)
    {
        if (!ActivitySource.HasListeners())
        {
            return null;
        }

        // ReSharper disable once ExplicitCallerInfoArgument
        return ActivityHelpers.ActivitySource.StartActivity(
            name: activityName,
            kind: ActivityKind.Internal,
            tags: [new(ActivityConstants.OutboxKeyTag, key)]);
    }
}

internal static class ActivityConstants
{
    public const string OutboxKeyTag = "outbox.key";
    public const string OutboxBatchSizeTag = "outbox.batch.size";
    public const string OutboxCleanedCountTag = "outbox.cleaned.count";
}

