using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using YakShaveFx.OutboxKit.Core.OpenTelemetry;

namespace YakShaveFx.OutboxKit.Core.CleanUp;

internal sealed partial class CleanUpBackgroundService(
    OutboxKey key,
    IOutboxCleaner cleaner,
    TimeProvider timeProvider,
    CoreCleanUpSettings settings,
    CleanerMetrics metrics,
    ILogger<CleanUpBackgroundService> logger) : BackgroundService
{
    private readonly TimeSpan _cleanUpInterval = settings.CleanUpInterval;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarting(logger, key.ProviderKey, key.ClientKey, _cleanUpInterval);

        await Task.Yield(); // just to let the startup continue, without waiting on this

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var activity = ActivityHelpers.StartActivity("clean processed outbox messages", key);
                try
                {
                    var cleaned = await cleaner.CleanAsync(stoppingToken);
                    metrics.MessagesCleaned(key, cleaned);
                    activity?.SetTag(ActivityConstants.OutboxCleanedCountTag, cleaned);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // expected when the service is stopping, let it stop gracefully
                    continue;
                }
                catch (Exception ex)
                {
                    // we don't want the background service to stop while the application continues, so catching and logging
                    LogUnexpectedError(logger, key.ProviderKey, key.ClientKey, ex);
                }

                await Task.Delay(_cleanUpInterval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // expected when the service is stopping, let it stop gracefully
            }
        }

        LogStopping(logger, key.ProviderKey, key.ClientKey);
    }

    [LoggerMessage(
        LogLevel.Debug,
        Message =
            "Starting outbox clean up service for provider key \"{providerKey}\" and client key \"{clientKey}\", with clean up interval {cleanUpInterval}")]
    private static partial void LogStarting(ILogger logger, string providerKey, string clientKey, TimeSpan cleanUpInterval);

    [LoggerMessage(LogLevel.Debug,
        Message = "Shutting down outbox clean up service for provider key \"{providerKey}\" and client key \"{clientKey}\"")]
    private static partial void LogStopping(ILogger logger, string providerKey, string clientKey);

    [LoggerMessage(LogLevel.Error,
        Message = "Unexpected error while cleaning outbox messages for provider key \"{providerKey}\" and client key \"{clientKey}\"")]
    private static partial void LogUnexpectedError(ILogger logger, string providerKey, string clientKey, Exception ex);
}