using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace YakShaveFx.OutboxKit.Core.Polling;

internal sealed partial class PollingBackgroundService(
    string key,
    IKeyedOutboxListener listener,
    IProducer producer,
    TimeProvider timeProvider,
    CorePollingSettings settings,
    ILogger<PollingBackgroundService> logger) : BackgroundService
{
    private readonly TimeSpan _pollingInterval = settings.PollingInterval;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarting(logger, key, _pollingInterval);

        await Task.Yield(); // just to let the startup continue, without waiting on the outbox

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                try
                {
                    await producer.ProducePendingAsync(key, stoppingToken);
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

                // to avoid letting the delays running in the background, wasting resources
                // we create a linked token, to cancel them
                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

                var listenerTask = listener.WaitForMessagesAsync(key, linkedTokenSource.Token);
                var delayTask = Task.Delay(_pollingInterval, timeProvider, linkedTokenSource.Token);

                // wait for whatever occurs first:
                // - being notified of new messages added to the outbox
                // - poll the outbox every x amount of time, for example, in cases where another instance of the service persisted
                //   something but didn't produce it, or some error occurred when producing and there are pending messages
                await Task.WhenAny(listenerTask, delayTask);

                LogWakeUp(logger, key, listenerTask.IsCompleted ? "listener triggered" : "polling interval elapsed");

                await linkedTokenSource.CancelAsync();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // expected when the service is stopping, let it stop gracefully
            }
        }

        LogStopping(logger, key);
    }

    [LoggerMessage(LogLevel.Debug,
        Message =
            "Starting outbox polling background service for key \"{key}\", with polling interval {pollingInterval}")]
    private static partial void LogStarting(ILogger logger, string key, TimeSpan pollingInterval);

    [LoggerMessage(LogLevel.Debug, Message = "Shutting down outbox polling background service for key \"{key}\"")]
    private static partial void LogStopping(ILogger logger, string key);

    [LoggerMessage(LogLevel.Debug,
        Message = "Waking up outbox polling background service for key \"{key}\", due to \"{reason}\"")]
    private static partial void LogWakeUp(ILogger logger, string key, string reason);

    [LoggerMessage(LogLevel.Error,
        Message = "Unexpected error while producing pending outbox messages for key \"{key}\"")]
    private static partial void LogUnexpectedError(ILogger logger, string key, Exception ex);
}