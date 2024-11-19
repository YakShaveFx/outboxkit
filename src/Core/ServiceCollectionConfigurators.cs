using Microsoft.Extensions.DependencyInjection;
using YakShaveFx.OutboxKit.Core.Polling;

namespace YakShaveFx.OutboxKit.Core;

/// <summary>
/// Allows configuring OutboxKit.
/// </summary>
public interface IOutboxKitConfigurator
{
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
        OutboxKey key,
        TPollingOutboxKitConfigurator configurator)
        where TPollingOutboxKitConfigurator : IPollingOutboxKitConfigurator, new();

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
        OutboxKey key,
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
    void ConfigureServices(OutboxKey key, IServiceCollection services);

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
    void ConfigureServices(OutboxKey key, IServiceCollection services);
}

internal sealed class OutboxKitConfigurator : IOutboxKitConfigurator
{
    private readonly Dictionary<OutboxKey, IPollingOutboxKitConfigurator> _pollingConfigurators = new();
    private readonly Dictionary<OutboxKey, IPushOutboxKitConfigurator> _pushConfigurators = new();

    public IReadOnlyDictionary<OutboxKey, IPollingOutboxKitConfigurator> PollingConfigurators
        => _pollingConfigurators;

    public IReadOnlyDictionary<OutboxKey, IPushOutboxKitConfigurator> PushConfigurators => _pushConfigurators;

    public IOutboxKitConfigurator WithPolling<TPollingOutboxKitConfigurator>(
        OutboxKey key,
        TPollingOutboxKitConfigurator configurator)
        where TPollingOutboxKitConfigurator : IPollingOutboxKitConfigurator, new()
    {
        _pollingConfigurators.Add(key, configurator);
        return this;
    }

    public IOutboxKitConfigurator WithPush<TPushOutboxKitConfigurator>(
        OutboxKey key,
        TPushOutboxKitConfigurator configurator)
        where TPushOutboxKitConfigurator : IPushOutboxKitConfigurator, new()
    {
        _pushConfigurators.Add(key, configurator);
        return this;
    }
}