using System.Diagnostics;

namespace YakShaveFx.OutboxKit.Core.OpenTelemetry;

internal static class ActivityHelpers
{
    private const string ActivitySourceName = "YakShaveFx.OutboxKit";

    internal static readonly ActivitySource ActivitySource = new(
        ActivitySourceName,
        typeof(ActivityHelpers).Assembly.GetName().Version!.ToString());
}

internal static class ActivityConstants
{
    public const string OutboxKeyTag = "outbox.key";
    public const string OutboxBatchSizeTag = "outbox.batch.size";
}

