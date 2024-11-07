using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using YakShaveFx.OutboxKit.Core.OpenTelemetry;
using YakShaveFx.OutboxKit.Core.Polling;

namespace YakShaveFx.OutboxKit.Core;

public static class ServiceCollectionExtensions
{
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
            // can't use AddHostedService, because it only adds one instance of the service
            services.AddSingleton<IHostedService>(s => new PollingBackgroundService(
                key,
                s.GetRequiredService<IKeyedOutboxListener>(),
                s.GetRequiredService<IProducer>(),
                s.GetRequiredService<TimeProvider>(),
                s.GetRequiredKeyedService<CorePollingSettings>(key),
                s.GetRequiredService<ILogger<PollingBackgroundService>>()));

            pollingConfigurator.ConfigureServices(key, services);
            services.AddKeyedSingleton(key, pollingConfigurator.GetCoreSettings());
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

public interface IOutboxKitConfigurator
{
    IOutboxKitConfigurator WithBatchProducer<TBatchProducer>()
        where TBatchProducer : class, IBatchProducer;

    /// <summary>
    /// <para>Configures outbox kit for polling, with a default key.</para>
    /// <para>Note: this method is mainly targeted at libraries implementing polling for specific databases,
    /// not really for end users, unless implementing a custom polling solution.</para>
    /// </summary>
    /// <param name="configurator">A database specific polling configurator.</param>
    /// <typeparam name="TPollingOutboxKitConfigurator">The type of database specific polling configurator.</typeparam>
    /// <returns>The current configurator instance, for method chaining.</returns>
    IOutboxKitConfigurator WithPolling<TPollingOutboxKitConfigurator>(TPollingOutboxKitConfigurator configurator)
        where TPollingOutboxKitConfigurator : IPollingOutboxKitConfigurator, new();

    /// <summary>
    /// <para>Configures outbox kit for polling, with a given key.</para>
    /// <para>Note: this method is mainly targeted at libraries implementing polling for specific databases,
    /// not really for end users, unless implementing a custom polling solution.</para>
    /// </summary>
    /// <param name="key">The key to associate with this polling instance, allowing for multiple instances running in tandem.</param>
    /// <param name="configurator">A database specific polling configurator.</param>
    /// <typeparam name="TPollingOutboxKitConfigurator">The type of database specific polling configurator.</typeparam>
    /// <returns>The current configurator instance, for method chaining.</returns>
    IOutboxKitConfigurator WithPolling<TPollingOutboxKitConfigurator>(
        string key,
        TPollingOutboxKitConfigurator configurator)
        where TPollingOutboxKitConfigurator : IPollingOutboxKitConfigurator, new();

    IOutboxKitConfigurator WithPush<TPushOutboxKitConfigurator>(
        TPushOutboxKitConfigurator configurator)
        where TPushOutboxKitConfigurator : IPushOutboxKitConfigurator, new();

    IOutboxKitConfigurator WithPush<TPushOutboxKitConfigurator>(
        string key,
        TPushOutboxKitConfigurator configurator)
        where TPushOutboxKitConfigurator : IPushOutboxKitConfigurator, new();
}

public interface IPollingOutboxKitConfigurator
{
    void ConfigureServices(string key, IServiceCollection services);

    CorePollingSettings GetCoreSettings();
}

public interface IPushOutboxKitConfigurator
{
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
        => WithPolling("default", configurator);

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
        => WithPush("default", configurator);

    public IOutboxKitConfigurator WithPush<TPushOutboxKitConfigurator>(
        string key,
        TPushOutboxKitConfigurator configurator)
        where TPushOutboxKitConfigurator : IPushOutboxKitConfigurator, new()
    {
        _pushConfigurators.Add(key, configurator);
        return this;
    }
}