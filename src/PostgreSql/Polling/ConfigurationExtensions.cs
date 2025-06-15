using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using YakShaveFx.OutboxKit.Core;
using YakShaveFx.OutboxKit.Core.CleanUp;
using YakShaveFx.OutboxKit.Core.Polling;
using YakShaveFx.OutboxKit.PostgreSql.CleanUp;
using YakShaveFx.OutboxKit.PostgreSql.Shared;

namespace YakShaveFx.OutboxKit.PostgreSql.Polling;

public static class OutboxKitConfiguratorExtensions
{
    /// <summary>
    /// Configures OutboxKit to use PostgreSql polling, with a default key.
    /// </summary>
    /// <param name="configurator">The <see cref="IOutboxKitConfigurator"/> to configure PostgreSQL polling.</param>
    /// <param name="configure">Function to configure OutboxKit with PostgreSQL polling</param>
    /// <returns>The <see cref="IOutboxKitConfigurator"/> instance for chaining calls.</returns>
    public static IOutboxKitConfigurator WithPostgreSqlPolling(
        this IOutboxKitConfigurator configurator,
        Action<IPostgreSqlPollingOutboxKitConfigurator> configure)
    {
        var pollingConfigurator = new PollingOutboxKitConfigurator();
        configure(pollingConfigurator);
        configurator.WithPolling(PostgreSqlPollingProvider.DefaultKey, pollingConfigurator);
        return configurator;
    }

    /// <summary>
    /// Configures OutboxKit to use PostgreSql polling, with a given key.
    /// </summary>
    /// <param name="configurator">The <see cref="IOutboxKitConfigurator"/> to configure PostgreSQL polling.</param>
    /// <param name="key">The key assigned to the outbox instance being configured.</param>
    /// <param name="configure">Function to configure OutboxKit with PostgreSQL polling</param>
    /// <returns>The <see cref="IOutboxKitConfigurator"/> instance for chaining calls.</returns>
    public static IOutboxKitConfigurator WithPostgreSqlPolling(
        this IOutboxKitConfigurator configurator,
        string key,
        Action<IPostgreSqlPollingOutboxKitConfigurator> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(key));
        var pollingConfigurator = new PollingOutboxKitConfigurator();
        configure(pollingConfigurator);
        configurator.WithPolling(PostgreSqlPollingProvider.CreateKey(key), pollingConfigurator);
        return configurator;
    }
}

/// <summary>
/// Allows configuring the PostgreSql polling outbox.
/// </summary>
public interface IPostgreSqlPollingOutboxKitConfigurator
{
    /// <summary>
    /// Configures the connection string to the PostgreSql database.
    /// </summary>
    /// <param name="connectionString">The connection string to the PostgreSql database.</param>
    /// <returns>The <see cref="IPostgreSqlPollingOutboxKitConfigurator"/> instance for chaining calls.</returns>
    IPostgreSqlPollingOutboxKitConfigurator WithConnectionString(string connectionString);

    /// <summary>
    /// Configures the outbox table.
    /// </summary>
    /// <param name="configure">A function to configure the outbox table.</param>
    /// <returns>The <see cref="IPostgreSqlPollingOutboxKitConfigurator"/> instance for chaining calls.</returns>
    IPostgreSqlPollingOutboxKitConfigurator WithTable(Action<IPostgreSqlOutboxTableConfigurator> configure);

    /// <summary>
    /// Configures the outbox polling interval.
    /// </summary>
    /// <param name="pollingInterval">The interval at which the outbox is polled for new messages.</param>
    /// <returns>The <see cref="IPostgreSqlPollingOutboxKitConfigurator"/> instance for chaining calls.</returns>
    IPostgreSqlPollingOutboxKitConfigurator WithPollingInterval(TimeSpan pollingInterval);

    /// <summary>
    /// Configures the amount of messages to fetch from the outbox in each batch.
    /// </summary>
    /// <param name="batchSize">The amount of messages to fetch from the outbox in each batch.</param>
    /// <returns>The <see cref="IPostgreSqlPollingOutboxKitConfigurator"/> instance for chaining calls.</returns>
    IPostgreSqlPollingOutboxKitConfigurator WithBatchSize(int batchSize);

    /// <summary>
    /// Configures the outbox to update processed messages, instead of deleting them.
    /// </summary>
    /// <param name="configure">A function to configure updating processed messages.</param>
    /// <returns>The <see cref="IPostgreSqlPollingOutboxKitConfigurator"/> instance for chaining calls.</returns>
    /// <remarks>OutboxKit assumes the "processed at" column is a <see cref="DateTime"/> in UTC,
    /// and uses <see cref="TimeProvider"/> to obtain the time when completing and cleaning up the messages.</remarks>
    IPostgreSqlPollingOutboxKitConfigurator WithUpdateProcessed(Action<IPostgreSqlUpdateProcessedConfigurator>? configure);
    
    /// <summary>
    /// Configures the outbox to use "SELECT ... FOR UPDATE" for concurrency control.
    /// </summary>
    /// <returns>The <see cref="IPostgreSqlPollingOutboxKitConfigurator"/> instance for chaining calls.</returns>
    /// <remarks>This is the default concurrency control if nothing is explicitly set.</remarks>
    IPostgreSqlPollingOutboxKitConfigurator WithSelectForUpdateConcurrencyControl();
    
    /// <summary>
    /// <para>Configures the outbox to use advisory locks for concurrency control.</para>
    /// <para>See <see href="https://www.postgresql.org/docs/current/explicit-locking.html#ADVISORY-LOCKS"/> for more info about this type of locks</para>
    /// </summary>
    /// <returns>The <see cref="IPostgreSqlPollingOutboxKitConfigurator"/> instance for chaining calls.</returns>
    /// <remarks>
    /// <para>In some scenarios, using advisory locks might provide performance benefits when compared to "SELECT ... FOR UPDATE",
    /// given it avoids locking the actual rows in the outbox table as they're being produced.
    /// Tests should be done to verify if there are benefits for a given scenario.</para>
    /// </remarks>
    IPostgreSqlPollingOutboxKitConfigurator WithAdvisoryLockConcurrencyControl();
}

/// <summary>
/// Allows configuring the outbox to update processed messages, instead of deleting them.
/// </summary>
public interface IPostgreSqlUpdateProcessedConfigurator
{
    /// <summary>
    /// Configures the interval at which processed messages are cleaned up.
    /// </summary>
    /// <param name="cleanUpInterval">The interval at which processed messages are cleaned up.</param>
    /// <returns>The <see cref="IPostgreSqlUpdateProcessedConfigurator"/> instance for chaining calls.</returns>
    IPostgreSqlUpdateProcessedConfigurator WithCleanUpInterval(TimeSpan cleanUpInterval);

    /// <summary>
    /// Configures the amount of time after which processed messages should be deleted.
    /// </summary>
    /// <param name="maxAge">The amount of time after which processed messages should be deleted.</param>
    /// <returns>The <see cref="IPostgreSqlUpdateProcessedConfigurator"/> instance for chaining calls.</returns>
    IPostgreSqlUpdateProcessedConfigurator WithMaxAge(TimeSpan maxAge);
}

internal sealed class PollingOutboxKitConfigurator : IPollingOutboxKitConfigurator, IPostgreSqlPollingOutboxKitConfigurator
{
    private readonly PostgreSqlOutboxTableConfigurator _tableConfigurator = new();
    private string? _connectionString;
    private CorePollingSettings _coreSettings = new();
    private PostgreSqlPollingSettings _settings = new();
    private PostgreSqlCleanUpSettings _cleanUpSettings = new();

    public IPostgreSqlPollingOutboxKitConfigurator WithConnectionString(string connectionString)
    {
        _connectionString = connectionString;
        return this;
    }

    public IPostgreSqlPollingOutboxKitConfigurator WithTable(Action<IPostgreSqlOutboxTableConfigurator> configure)
    {
        configure(_tableConfigurator);
        return this;
    }

    public IPostgreSqlPollingOutboxKitConfigurator WithPollingInterval(TimeSpan pollingInterval)
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

    public IPostgreSqlPollingOutboxKitConfigurator WithBatchSize(int batchSize)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Batch size must be greater than zero");
        }

        _settings = _settings with { BatchSize = batchSize };
        return this;
    }

    public IPostgreSqlPollingOutboxKitConfigurator WithUpdateProcessed(Action<IPostgreSqlUpdateProcessedConfigurator>? configure)
    {
        var cfg = new PostgreSqlUpdateProcessedConfigurator();
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
    
    public IPostgreSqlPollingOutboxKitConfigurator WithSelectForUpdateConcurrencyControl()
    {
        _settings = _settings with { ConcurrencyControl = ConcurrencyControl.SelectForUpdate };
        return this;
    }
    
    public IPostgreSqlPollingOutboxKitConfigurator WithAdvisoryLockConcurrencyControl()
    {
        _settings = _settings with { ConcurrencyControl = ConcurrencyControl.AdvisoryLock };
        return this;
    }

    public void ConfigureServices(OutboxKey key, IServiceCollection services)
    {
        if (_connectionString is null)
        {
            throw new InvalidOperationException($"Connection string must be set for PostgreSql polling with key \"{key}\"");
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
                s.GetRequiredKeyedService<NpgsqlDataSource>(key),
                s.GetRequiredService<TimeProvider>()));
        }

        services
            .AddNpgsqlDataSource(_connectionString, serviceKey: key)
            .AddKeyedSingleton<IBatchFetcher>(
                key,
                (s, _) =>
                    _settings.ConcurrencyControl switch
                    {
                        ConcurrencyControl.SelectForUpdate => new SelectForUpdateBatchFetcher(
                            _settings,
                            tableCfg,
                            s.GetRequiredKeyedService<NpgsqlDataSource>(key),
                            s.GetRequiredService<TimeProvider>()),
                        ConcurrencyControl.AdvisoryLock => new AdvisoryLockBatchFetcher(
                            _settings,
                            tableCfg,
                            s.GetRequiredKeyedService<NpgsqlDataSource>(key),
                            s.GetRequiredService<TimeProvider>()),
                        _ => throw new InvalidOperationException($"Invalid concurrency control {_settings.ConcurrencyControl}")
                    });
    }

    public CorePollingSettings GetCoreSettings() => _coreSettings;
}

internal class PostgreSqlUpdateProcessedConfigurator : IPostgreSqlUpdateProcessedConfigurator
{
    public TimeSpan CleanUpInterval { get; private set; } = TimeSpan.Zero;
    public TimeSpan MaxAge { get; private set; } = TimeSpan.Zero;

    public IPostgreSqlUpdateProcessedConfigurator WithCleanUpInterval(TimeSpan cleanUpInterval)
    {
        CleanUpInterval = cleanUpInterval;
        return this;
    }

    public IPostgreSqlUpdateProcessedConfigurator WithMaxAge(TimeSpan maxAge)
    {
        MaxAge = maxAge;
        return this;
    }
}

internal sealed record PostgreSqlPollingSettings
{
    public int BatchSize { get; init; } = 100;

    public CompletionMode CompletionMode { get; init; } = CompletionMode.Delete;
    
    public ConcurrencyControl ConcurrencyControl { get; init; } = ConcurrencyControl.SelectForUpdate;
    
}

internal enum CompletionMode
{
    Delete,
    Update
}

internal enum ConcurrencyControl
{
    SelectForUpdate,
    AdvisoryLock
}