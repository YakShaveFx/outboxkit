using System.Diagnostics;
using Microsoft.Extensions.Logging;
using YakShaveFx.OutboxKit.Core.OpenTelemetry;

namespace YakShaveFx.OutboxKit.Core.Polling;

internal interface ICompletionRetryCollector
{
    void Collect(IReadOnlyCollection<IMessage> messages);
}

internal interface ICompletionRetrier
{
    ValueTask RetryAsync(CancellationToken ct);
}

// not thread safe, as it is only used in the context of a producing flow, which has no concurrency
internal sealed partial class CompletionRetrier(
    OutboxKey key,
    IBatchCompleteRetrier providerCompletionRetrier,
    RetrierBuilderFactory retrierBuilderFactory,
    CompletionRetrierMetrics metrics,
    ILogger<CompletionRetrier> logger)
    : ICompletionRetryCollector, ICompletionRetrier
{
    private readonly Retrier _retrier = retrierBuilderFactory.Create()
        .WithMaxRetries(int.MaxValue)
        .WithShouldRetryDecider(ex =>
        {
            // retry on all exceptions except cancellation
            if (ex is OperationCanceledException oce) return oce.CancellationToken == CancellationToken.None;
            return true;
        })
        .Build();

    private List<IMessage> _messages = [];

    public void Collect(IReadOnlyCollection<IMessage> messages)
    {
        _messages.AddRange(messages);
        metrics.NewMessagesPendingRetry(key, messages.Count);
    }

    public ValueTask RetryAsync(CancellationToken ct)
        => _messages.Count == 0
            ? ValueTask.CompletedTask
            : new(InnerRetryAsync(ct));

    private async Task InnerRetryAsync(CancellationToken ct) 
        => await _retrier.ExecuteWithRetryAsync(
            async () =>
            {
                metrics.CompletionRetryAttempted(key, _messages.Count);
                using var activity = ActivityHelpers.StartActivity(
                    "retrying produced messages completion",
                    key,
                    [new(ActivityConstants.OutboxProducedMessagesToCompleteTag, _messages.Count)]);

                try
                {
                    await providerCompletionRetrier.RetryAsync(_messages, ct);
                    
                    metrics.MessagesCompleted(key, _messages.Count);
                    
                    // since most of the time there are no messages to retry, we clear messages by creating a new list,
                    // so the old one can be garbage collected, avoiding the underlying array to be kept in memory
                    _messages = [];
                }
                catch (Exception ex)
                {
                    LogRetryFailed(logger, key.ProviderKey, key.ClientKey, ex);
                    activity?.SetStatus(ActivityStatusCode.Error);
                    activity?.RecordException(ex, new TagList
                    {
                        { ActivityConstants.OutboxProviderKeyTag, key.ProviderKey },
                        { ActivityConstants.OutboxClientKeyTag, key.ClientKey }
                    });
                    throw;
                }
            },
            ct);
    
    [LoggerMessage(LogLevel.Error,
        Message =
            "Error while retrying message completion for provider key \"{providerKey}\" and client key \"{clientKey}\"")]
    private static partial void LogRetryFailed(ILogger logger, string providerKey, string clientKey, Exception ex);
}