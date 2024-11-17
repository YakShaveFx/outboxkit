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

        if (configurator.PollingConfigurators.Count > 0)
        {
            AddOutboxKitPolling(services, configurator);
        }

        if (configurator.PushConfigurators.Count > 0)
        {
            AddOutboxKitPush(services, configurator);
        }

        return services;
    }

    private static void AddOutboxKitPolling(IServiceCollection services, OutboxKitConfigurator configurator)
    {
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
                return KeyedListener.Create([..keys]);
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
                s.GetRequiredKeyedService<IPollingProducer>(key),
                s.GetRequiredService<TimeProvider>(),
                corePollingSettings,
                s.GetRequiredService<ILogger<PollingBackgroundService>>()));

            services.AddKeyedSingleton<IPollingProducer>(key, (s, _) => new PollingProducer(
                key,
                s.GetRequiredKeyedService<IBatchFetcher>(key),
                s.GetRequiredService<IBatchProducer>(),
                s.GetRequiredService<ProducerMetrics>()));
            
            if (corePollingSettings.EnableCleanUp)
            {
                services.TryAddSingleton<CleanerMetrics>();
                services.AddSingleton<IHostedService>(s => new CleanUpBackgroundService(
                    key,
                    s.GetRequiredKeyedService<IOutboxCleaner>(key),
                    s.GetRequiredService<TimeProvider>(),
                    cleanUpSettings,
                    s.GetRequiredService<CleanerMetrics>(),
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
