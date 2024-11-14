using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using YakShaveFx.OutboxKit.Core.OpenTelemetry;
using YakShaveFx.OutboxKit.Core.Polling;

namespace YakShaveFx.OutboxKit.Core.CleanUp;

internal sealed partial class CleanUpBackgroundService(
    string key,
    TimeProvider timeProvider,
    CoreCleanUpSettings settings,
    CleanerMetrics metrics,
    IServiceScopeFactory scopeFactory,
    ILogger<CleanUpBackgroundService> logger) : BackgroundService
{
    private readonly TimeSpan _cleanUpInterval = settings.CleanUpInterval;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarting(logger, key, _cleanUpInterval);

        await Task.Yield(); // just to let the startup continue, without waiting on this

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var activity = ActivityHelpers.StartActivity("clean processed outbox messages", key);
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var cleaner = scope.ServiceProvider.GetRequiredKeyedService<IOutboxCleaner>(key);
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
                    LogUnexpectedError(logger, key, ex);
                }
                
                await Task.Delay(_cleanUpInterval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // expected when the service is stopping, let it stop gracefully
            }
        }

        LogStopping(logger, key);
    }

    [LoggerMessage(
        LogLevel.Debug,
        Message = "Starting outbox clean up service for key \"{key}\", with clean up interval {cleanUpInterval}")]
    private static partial void LogStarting(ILogger logger, string key, TimeSpan cleanUpInterval);

    [LoggerMessage(LogLevel.Debug, Message = "Shutting down outbox clean up service for key \"{key}\"")]
    private static partial void LogStopping(ILogger logger, string key);

    [LoggerMessage(LogLevel.Error,
        Message = "Unexpected error while cleaning outbox messages for key \"{key}\"")]
    private static partial void LogUnexpectedError(ILogger logger, string key, Exception ex);
}