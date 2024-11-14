using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using YakShaveFx.OutboxKit.Core.CleanUp;
using YakShaveFx.OutboxKit.Core.OpenTelemetry;
using YakShaveFx.OutboxKit.Core.Polling;

namespace YakShaveFx.OutboxKit.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds OutboxKit services to the provided <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to configure.</param>
    /// <param name="configure">Function to configure OutboxKit.</param>
    /// <returns>The supplied <see cref="IServiceCollection"/> for chaining calls.</returns>
    public static IServiceCollection AddOutboxKit(
        this IServiceCollection services,
        Action<IOutboxKitConfigurator> configure)
    {
        var configurator = new OutboxKitConfigurator();
        configure(configurator);

        AddBatchProducerProvider(services, configurator);

        if (configurator.PollingConfigurators.Count > 0)
        {
            AddOutboxKitPolling(services, configurator);
        }

        if (configurator.PushConfigurators.Count > 0)
        {
            AddOutboxKitPush(services, configurator);
        }

        return services;

        static void AddBatchProducerProvider(IServiceCollection services, OutboxKitConfigurator configurator)
            // normally singleton would suffice, but in case the user registered the producer as scoped, we use scoped as well to support any of the 3 options
            => services.AddScoped<IBatchProducerProvider>(
                s => new BatchProducerProvider(configurator.BatchProducerType, s));
    }

    private static void AddOutboxKitPolling(IServiceCollection services, OutboxKitConfigurator configurator)
    {
        services.AddSingleton<IProducer, Producer>();
        services.AddSingleton<ProducerMetrics>();

        if (configurator.PollingConfigurators.Count == 1)
        {
            services.AddSingleton<Listener>();
            services.AddSingleton<IOutboxListener>(s => s.GetRequiredService<Listener>());
            services.AddSingleton<IOutboxTrigger>(s => s.GetRequiredService<Listener>());
            services.AddSingleton<IKeyedOutboxListener>(s => s.GetRequiredService<Listener>());
            services.AddSingleton<IKeyedOutboxTrigger>(s => s.GetRequiredService<Listener>());
        }
        else
        {
            services.AddSingleton<KeyedListener>(s =>
            {
                var keys = configurator.PollingConfigurators.Keys;
                return new KeyedListener(keys);
            });
            services.AddSingleton<IKeyedOutboxListener>(s => s.GetRequiredService<KeyedListener>());
            services.AddSingleton<IKeyedOutboxTrigger>(s => s.GetRequiredService<KeyedListener>());
        }

        foreach (var (key, pollingConfigurator) in configurator.PollingConfigurators)
        {
            var corePollingSettings = pollingConfigurator.GetCoreSettings();
            var cleanUpSettings = new CoreCleanUpSettings
            {
                CleanUpInterval = corePollingSettings.CleanUpInterval
            };

            // can't use AddHostedService, because it only adds one instance of the service
            services.AddSingleton<IHostedService>(s => new PollingBackgroundService(
                key,
                s.GetRequiredService<IKeyedOutboxListener>(),
                s.GetRequiredService<IProducer>(),
                s.GetRequiredService<TimeProvider>(),
                s.GetRequiredKeyedService<CorePollingSettings>(key),
                s.GetRequiredService<ILogger<PollingBackgroundService>>()));

            if (corePollingSettings.EnableCleanUp)
            {
                services.TryAddSingleton<CleanerMetrics>();
                services.AddSingleton<IHostedService>(s => new CleanUpBackgroundService(
                    key,
                    s.GetRequiredService<TimeProvider>(),
                    cleanUpSettings,
                    s.GetRequiredService<CleanerMetrics>(),
                    s.GetRequiredService<IServiceScopeFactory>(),
                    s.GetRequiredService<ILogger<CleanUpBackgroundService>>()));
            }
            
            pollingConfigurator.ConfigureServices(key, services);

            services.AddKeyedSingleton(key, corePollingSettings);
        }
    }

    private static void AddOutboxKitPush(IServiceCollection services, OutboxKitConfigurator configurator)
    {
        foreach (var (key, pushConfigurator) in configurator.PushConfigurators)
        {
            pushConfigurator.ConfigureServices(key, services);
        }
    }
}

internal static class SetupConstants
{
    public const string DefaultKey = "default";
}

/// <summary>
/// Allows configuring OutboxKit.
/// </summary>
public interface IOutboxKitConfigurator
{
    /// <summary>
    /// Configures the type implementing <see cref="IBatchProducer"/>, to produce the messages stored in the outbox.
    /// </summary>
    /// <typeparam name="TBatchProducer">The type implementing <see cref="IBatchProducer"/>.</typeparam>
    /// <returns>The <see cref="IOutboxKitConfigurator"/> instance for chaining calls.</returns>
    IOutboxKitConfigurator WithBatchProducer<TBatchProducer>() where TBatchProducer : class, IBatchProducer;

    /// <summary>
    /// Configures OutboxKit for polling, with a default key.
    /// </summary>
    /// <param name="configurator">A database specific polling configurator.</param>
    /// <typeparam name="TPollingOutboxKitConfigurator">The type of database specific polling configurator.</typeparam>
    /// <returns>The <see cref="IOutboxKitConfigurator"/> instance for chaining calls.</returns>
    /// <remarks>Note: this method is mainly targeted at libraries implementing polling for specific databases,
    /// not really for end users, unless implementing a custom polling solution.</remarks>
    IOutboxKitConfigurator WithPolling<TPollingOutboxKitConfigurator>(TPollingOutboxKitConfigurator configurator)
        where TPollingOutboxKitConfigurator : IPollingOutboxKitConfigurator, new();

    /// <summary>
    /// Configures outbox kit for polling, with a given key.
    /// </summary>
    /// <param name="key">The key to associate with this polling instance, allowing for multiple instances running in tandem.</param>
    /// <param name="configurator">A database specific polling configurator.</param>
    /// <typeparam name="TPollingOutboxKitConfigurator">The type of database specific polling configurator.</typeparam>
    /// <returns>The <see cref="IOutboxKitConfigurator"/> instance for chaining calls.</returns>
    /// <remarks>Note: this method is mainly targeted at libraries implementing polling for specific databases,
    /// not really for end users, unless implementing a custom polling solution.</remarks>
    IOutboxKitConfigurator WithPolling<TPollingOutboxKitConfigurator>(
        string key,
        TPollingOutboxKitConfigurator configurator)
        where TPollingOutboxKitConfigurator : IPollingOutboxKitConfigurator, new();

    /// <summary>
    /// Configures OutboxKit for push, with a default key.
    /// </summary>
    /// <param name="configurator">A database specific push configurator.</param>
    /// <typeparam name="TPushOutboxKitConfigurator">The type of database specific push configurator.</typeparam>
    /// <returns>The <see cref="IOutboxKitConfigurator"/> instance for chaining calls.</returns>
    /// <remarks>Note: this method is mainly targeted at libraries implementing polling for specific databases,
    /// not really for end users, unless implementing a custom polling solution.</remarks>
    IOutboxKitConfigurator WithPush<TPushOutboxKitConfigurator>(
        TPushOutboxKitConfigurator configurator)
        where TPushOutboxKitConfigurator : IPushOutboxKitConfigurator, new();

    /// <summary>
    /// Configures OutboxKit for push, with a given key.
    /// </summary>
    /// <param name="key">The key to associate with this push instance, allowing for multiple instances running in tandem.</param>
    /// <param name="configurator">A database specific push configurator.</param>
    /// <typeparam name="TPushOutboxKitConfigurator">The type of database specific push configurator.</typeparam>
    /// <returns>The <see cref="IOutboxKitConfigurator"/> instance for chaining calls.</returns>
    /// <remarks>Note: this method is mainly targeted at libraries implementing polling for specific databases,
    /// not really for end users, unless implementing a custom polling solution.</remarks>
    IOutboxKitConfigurator WithPush<TPushOutboxKitConfigurator>(
        string key,
        TPushOutboxKitConfigurator configurator)
        where TPushOutboxKitConfigurator : IPushOutboxKitConfigurator, new();
}

/// <summary>
/// To be implemented by specific database polling providers, to slot the setup into the OutboxKit pipeline.
/// </summary>
public interface IPollingOutboxKitConfigurator
{
    /// <summary>
    /// Allows configuring the polling implementation provider services for the given key.
    /// </summary>
    /// <param name="key">The key to associate with the polling outbox instance, allowing for multiple instances running in tandem.</param>
    /// <param name="services">The <see cref="IServiceCollection"/> to configure.</param>
    void ConfigureServices(string key, IServiceCollection services);

    /// <summary>
    /// Gets core settings required by OutboxKit, which are collected by the custom implementation, for end user configuration ease.
    /// </summary>
    /// <returns>The collected <see cref="CorePollingSettings"/>.</returns>
    CorePollingSettings GetCoreSettings();
}

/// <summary>
/// To be implemented by specific database push providers, to slot the setup into the OutboxKit pipeline.
/// </summary>
public interface IPushOutboxKitConfigurator
{
    /// <summary>
    /// Allows configuring the push implementation provider services for the given key.
    /// </summary>
    /// <param name="key">The key to associate with the push outbox instance, allowing for multiple instances running in tandem.</param>
    /// <param name="services">The <see cref="IServiceCollection"/> to configure.</param>
    void ConfigureServices(string key, IServiceCollection services);
}

internal sealed class OutboxKitConfigurator : IOutboxKitConfigurator
{
    private readonly Dictionary<string, IPollingOutboxKitConfigurator> _pollingConfigurators = new();
    private readonly Dictionary<string, IPushOutboxKitConfigurator> _pushConfigurators = new();
    private Type? _batchProducerType;

    public Type BatchProducerType
        => _batchProducerType ?? throw new InvalidOperationException("Batch producer type not set");

    public IReadOnlyDictionary<string, IPollingOutboxKitConfigurator> PollingConfigurators => _pollingConfigurators;
    public IReadOnlyDictionary<string, IPushOutboxKitConfigurator> PushConfigurators => _pushConfigurators;

    public IOutboxKitConfigurator WithBatchProducer<TBatchProducer>()
        where TBatchProducer : class, IBatchProducer
    {
        _batchProducerType = typeof(TBatchProducer);
        return this;
    }

    public IOutboxKitConfigurator WithPolling<TPollingOutboxKitConfigurator>(
        TPollingOutboxKitConfigurator configurator)
        where TPollingOutboxKitConfigurator : IPollingOutboxKitConfigurator, new()
        => WithPolling(SetupConstants.DefaultKey, configurator);

    public IOutboxKitConfigurator WithPolling<TPollingOutboxKitConfigurator>(
        string key,
        TPollingOutboxKitConfigurator configurator)
        where TPollingOutboxKitConfigurator : IPollingOutboxKitConfigurator, new()
    {
        _pollingConfigurators.Add(key, configurator);
        return this;
    }

    public IOutboxKitConfigurator WithPush<TPushOutboxKitConfigurator>(
        TPushOutboxKitConfigurator configurator)
        where TPushOutboxKitConfigurator : IPushOutboxKitConfigurator, new()
        => WithPush(SetupConstants.DefaultKey, configurator);

    public IOutboxKitConfigurator WithPush<TPushOutboxKitConfigurator>(
        string key,
        TPushOutboxKitConfigurator configurator)
        where TPushOutboxKitConfigurator : IPushOutboxKitConfigurator, new()
    {
        _pushConfigurators.Add(key, configurator);
        return this;
    }
}