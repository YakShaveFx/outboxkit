using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MySqlConnector;
using YakShaveFx.OutboxKit.Core;
using YakShaveFx.OutboxKit.Core.CleanUp;
using YakShaveFx.OutboxKit.Core.Polling;
using YakShaveFx.OutboxKit.MySql.CleanUp;
using YakShaveFx.OutboxKit.MySql.Shared;

namespace YakShaveFx.OutboxKit.MySql.Polling;

public static class OutboxKitConfiguratorExtensions
{
    /// <summary>
    /// Configures OutboxKit to use MySql polling, with a default key.
    /// </summary>
    /// <param name="configurator">The <see cref="IOutboxKitConfigurator"/> to configure MySQL polling.</param>
    /// <param name="configure">Function to configure OutboxKit with MySQL polling</param>
    /// <returns>The <see cref="IOutboxKitConfigurator"/> instance for chaining calls.</returns>
    public static IOutboxKitConfigurator WithMySqlPolling(
        this IOutboxKitConfigurator configurator,
        Action<IMySqlPollingOutboxKitConfigurator> configure)
    {
        var pollingConfigurator = new PollingOutboxKitConfigurator();
        configure(pollingConfigurator);
        configurator.WithPolling(MySqlPollingProviderInfo.DefaultKey, pollingConfigurator);
        return configurator;
    }

    /// <summary>
    /// Configures OutboxKit to use MySql polling, with a given key.
    /// </summary>
    /// <param name="configurator">The <see cref="IOutboxKitConfigurator"/> to configure MySQL polling.</param>
    /// <param name="key">The key assigned to the outbox instance being configured.</param>
    /// <param name="configure">Function to configure OutboxKit with MySQL polling</param>
    /// <returns>The <see cref="IOutboxKitConfigurator"/> instance for chaining calls.</returns>
    public static IOutboxKitConfigurator WithMySqlPolling(
        this IOutboxKitConfigurator configurator,
        string key,
        Action<IMySqlPollingOutboxKitConfigurator> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(key));
        var pollingConfigurator = new PollingOutboxKitConfigurator();
        configure(pollingConfigurator);
        configurator.WithPolling(MySqlPollingProviderInfo.CreateKey(key), pollingConfigurator);
        return configurator;
    }
}

/// <summary>
/// Allows configuring the MySql polling outbox.
/// </summary>
public interface IMySqlPollingOutboxKitConfigurator
{
    /// <summary>
    /// Configures the connection string to the MySql database.
    /// </summary>
    /// <param name="connectionString">The connection string to the MySql database.</param>
    /// <returns>The <see cref="IMySqlPollingOutboxKitConfigurator"/> instance for chaining calls.</returns>
    IMySqlPollingOutboxKitConfigurator WithConnectionString(string connectionString);

    /// <summary>
    /// Configures the outbox table.
    /// </summary>
    /// <param name="configure">A function to configure the outbox table.</param>
    /// <returns>The <see cref="IMySqlPollingOutboxKitConfigurator"/> instance for chaining calls.</returns>
    IMySqlPollingOutboxKitConfigurator WithTable(Action<IMySqlOutboxTableConfigurator> configure);

    /// <summary>
    /// Configures the outbox polling interval.
    /// </summary>
    /// <param name="pollingInterval">The interval at which the outbox is polled for new messages.</param>
    /// <returns>The <see cref="IMySqlPollingOutboxKitConfigurator"/> instance for chaining calls.</returns>
    IMySqlPollingOutboxKitConfigurator WithPollingInterval(TimeSpan pollingInterval);

    /// <summary>
    /// Configures the amount of messages to fetch from the outbox in each batch.
    /// </summary>
    /// <param name="batchSize">The amount of messages to fetch from the outbox in each batch.</param>
    /// <returns>The <see cref="IMySqlPollingOutboxKitConfigurator"/> instance for chaining calls.</returns>
    IMySqlPollingOutboxKitConfigurator WithBatchSize(int batchSize);

    /// <summary>
    /// Configures the outbox to update processed messages, instead of deleting them.
    /// </summary>
    /// <param name="configure">A function to configure updating processed messages.</param>
    /// <returns>The <see cref="IMySqlPollingOutboxKitConfigurator"/> instance for chaining calls.</returns>
    /// <remarks>OutboxKit assumes the "processed at" column is a <see cref="DateTime"/> in UTC,
    /// and uses <see cref="TimeProvider"/> to obtain the timw when completing and cleaning up the messages.</remarks>
    IMySqlPollingOutboxKitConfigurator WithUpdateProcessed(Action<IMySqlUpdateProcessedConfigurator>? configure);
}

/// <summary>
/// Allows configuring the outbox to update processed messages, instead of deleting them.
/// </summary>
public interface IMySqlUpdateProcessedConfigurator
{
    /// <summary>
    /// Configures the interval at which processed messages are cleaned up.
    /// </summary>
    /// <param name="cleanUpInterval">The interval at which processed messages are cleaned up.</param>
    /// <returns>The <see cref="IMySqlUpdateProcessedConfigurator"/> instance for chaining calls.</returns>
    IMySqlUpdateProcessedConfigurator WithCleanUpInterval(TimeSpan cleanUpInterval);

    /// <summary>
    /// Configures the amount of time after which processed messages should be deleted.
    /// </summary>
    /// <param name="maxAge">The amount of time after which processed messages should be deleted.</param>
    /// <returns>The <see cref="IMySqlUpdateProcessedConfigurator"/> instance for chaining calls.</returns>
    IMySqlUpdateProcessedConfigurator WithMaxAge(TimeSpan maxAge);
}

internal sealed class PollingOutboxKitConfigurator : IPollingOutboxKitConfigurator, IMySqlPollingOutboxKitConfigurator
{
    private readonly MySqlOutboxTableConfigurator _tableConfigurator = new();
    private string? _connectionString;
    private CorePollingSettings _coreSettings = new();
    private MySqlPollingSettings _settings = new();
    private MySqlCleanUpSettings _cleanUpSettings = new();

    public IMySqlPollingOutboxKitConfigurator WithConnectionString(string connectionString)
    {
        _connectionString = connectionString;
        return this;
    }

    public IMySqlPollingOutboxKitConfigurator WithTable(Action<IMySqlOutboxTableConfigurator> configure)
    {
        configure(_tableConfigurator);
        return this;
    }

    public IMySqlPollingOutboxKitConfigurator WithPollingInterval(TimeSpan pollingInterval)
    {
        if (pollingInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pollingInterval),
                pollingInterval,
                "Polling interval must be greater than zero");
        }

        _coreSettings = _coreSettings with { PollingInterval = pollingInterval };
        return this;
    }

    public IMySqlPollingOutboxKitConfigurator WithBatchSize(int batchSize)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Batch size must be greater than zero");
        }

        _settings = _settings with { BatchSize = batchSize };
        return this;
    }

    public IMySqlPollingOutboxKitConfigurator WithUpdateProcessed(Action<IMySqlUpdateProcessedConfigurator>? configure)
    {
        var cfg = new MySqlUpdateProcessedConfigurator();
        configure?.Invoke(cfg);
        _settings = _settings with
        {
            CompletionMode = CompletionMode.Update
        };
        _cleanUpSettings = _cleanUpSettings with
        {
            MaxAge = cfg.MaxAge != TimeSpan.Zero ? cfg.MaxAge : _cleanUpSettings.MaxAge
        };
        _coreSettings = _coreSettings with
        {
            EnableCleanUp = true,
            CleanUpInterval = cfg.CleanUpInterval != TimeSpan.Zero ? cfg.CleanUpInterval : _coreSettings.CleanUpInterval
        };
        return this;
    }

    public void ConfigureServices(OutboxKey key, IServiceCollection services)
    {
        if (_connectionString is null)
        {
            throw new InvalidOperationException($"Connection string must be set for MySql polling with key \"{key}\"");
        }

        var tableCfg = _tableConfigurator.BuildConfiguration();

        if (_settings.CompletionMode == CompletionMode.Update && string.IsNullOrWhiteSpace(tableCfg.ProcessedAtColumn))
        {
            throw new InvalidOperationException("Processed at column must be set when updating processed messages");
        }

        services.TryAddSingleton(TimeProvider.System);

        if (_settings.CompletionMode == CompletionMode.Update)
        {
            services.AddKeyedSingleton(key, _cleanUpSettings);
            services.AddKeyedSingleton<IOutboxCleaner>(key, (s, _) => new Cleaner(
                tableCfg,
                _cleanUpSettings,
                s.GetRequiredKeyedService<MySqlDataSource>(key),
                s.GetRequiredService<TimeProvider>()));
        }

        services
            .AddKeyedMySqlDataSource(key, _connectionString)
            .AddKeyedSingleton<IBatchFetcher>(
                key,
                (s, _) => new BatchFetcher(
                    _settings,
                    tableCfg,
                    s.GetRequiredKeyedService<MySqlDataSource>(key),
                    s.GetRequiredService<TimeProvider>()));
    }

    public CorePollingSettings GetCoreSettings() => _coreSettings;
}

internal class MySqlUpdateProcessedConfigurator : IMySqlUpdateProcessedConfigurator
{
    public TimeSpan CleanUpInterval { get; private set; } = TimeSpan.Zero;
    public TimeSpan MaxAge { get; private set; } = TimeSpan.Zero;

    public IMySqlUpdateProcessedConfigurator WithCleanUpInterval(TimeSpan cleanUpInterval)
    {
        CleanUpInterval = cleanUpInterval;
        return this;
    }

    public IMySqlUpdateProcessedConfigurator WithMaxAge(TimeSpan maxAge)
    {
        MaxAge = maxAge;
        return this;
    }
}

internal sealed record MySqlPollingSettings
{
    public int BatchSize { get; init; } = 100;

    public CompletionMode CompletionMode { get; init; } = CompletionMode.Delete;
}

internal enum CompletionMode
{
    Delete,
    Update
}