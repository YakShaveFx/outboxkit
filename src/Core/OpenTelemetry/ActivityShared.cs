using System.Diagnostics;
using System.Globalization;

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

internal static class ActivityExtensions
{
    // copied and adapted from an older version of the OpenTelemetry.Api package
    // an equivalent AddException was added in .NET 9, but this library targets .NET 8 right now
    // to remove when we upgrade to targeting .NET 10
    public static Activity RecordException(this Activity activity, Exception ex, in TagList tags)
    {
        const string exceptionEventName = "exception";
        const string exceptionMessageTag = "exception.message";
        const string exceptionStackTraceTag = "exception.stacktrace";
        const string exceptionTypeTag = "exception.type";


        var tagsCollection = new ActivityTagsCollection
        {
            { exceptionTypeTag, ex.GetType().FullName },
            { exceptionStackTraceTag, ex.ToInvariantString() },
        };

        if (!string.IsNullOrWhiteSpace(ex.Message))
        {
            tagsCollection.Add(exceptionMessageTag, ex.Message);
        }

        foreach (var tag in tags)
        {
            tagsCollection[tag.Key] = tag.Value;
        }

        activity.AddEvent(new ActivityEvent(exceptionEventName, tags: tagsCollection));

        return activity;
    }

    private static string ToInvariantString(this Exception exception)
    {
        var originalUiCulture = Thread.CurrentThread.CurrentUICulture;

        try
        {
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
            return exception.ToString();
        }
        finally
        {
            Thread.CurrentThread.CurrentUICulture = originalUiCulture;
        }
    }
}