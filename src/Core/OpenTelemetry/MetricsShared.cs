namespace YakShaveFx.OutboxKit.Core.OpenTelemetry;

internal static class MetricsConstants
{
    private const string MeterNamePrefix = "outboxkit.";

    public static class PollingBackgroundService
    {
        public static class PollingCycles
        {
            public const string Name = MeterNamePrefix + "polling_cycles";
            public const string Unit = "{cycle}";
            public const string Description = "The number of polling cycles executed by the background service";
            
            public static class Tags
            {
                public const string Result = "result";
            }
        }
    }

    public static class CleanUpBackgroundService
    {
        public static class CleanedMessages
        {
            public const string Name = MeterNamePrefix + "cleaned_messages";
            public const string Unit = "{message}";
            public const string Description = "The number of processed outbox messages cleaned";
        }
    }

    public static class CompletionRetrier
    {
        public static class CompletionRetryAttempts
        {
            public const string Name = MeterNamePrefix + "completion_retry_attempts";
            public const string Unit = "{attempt}";
            public const string Description = "The number of attempts to retry completion of produced messages";
        }

        public static class CompletionRetriedMessages
        {
            public const string Name = MeterNamePrefix + "completion_retried_messages";
            public const string Unit = "{message}";
            public const string Description = "The number of messages for which completion was retried (retrying the same message multiple times counts as one message)";
        }

        public static class PendingRetry
        {
            public const string Name = MeterNamePrefix + "messages_pending_completion_retry";
            public const string Unit = "{message}";
            public const string Description = "The number of messages pending completion retry";
        }
    }

    public static class PollingProducer
    {
        public static class ProducedBatches
        {
            public const string Name = MeterNamePrefix + "produced_batches";
            public const string Unit = "{batch}";
            public const string Description = "The number of batches produced";

            public static class Tags
            {
                public const string AllMessagesProduced = "all_messages_produced";
            }
        }

        public static class ProducedMessages
        {
            public const string Name = MeterNamePrefix + "produced_messages";
            public const string Unit = "{message}";
            public const string Description = "The number of messages produced";
        }
    }

    public static class Shared
    {
        public static class Tags
        {
            public const string ProviderKeyTag = "provider_key";
            public const string ClientKeyTag = "client_key";
        }
    }
}