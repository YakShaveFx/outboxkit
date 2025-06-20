using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using YakShaveFx.OutboxKit.Core;
using YakShaveFx.OutboxKit.Core.CleanUp;
using YakShaveFx.OutboxKit.Core.Polling;
using YakShaveFx.OutboxKit.MongoDb.CleanUp;
using YakShaveFx.OutboxKit.MongoDb.Synchronization;

namespace YakShaveFx.OutboxKit.MongoDb.Polling;

internal interface IGetMongoDbOutboxCollectionConfigured
{
    void ConfigureCollection<TMessage, TId>(MongoDbPollingCollectionSettings<TMessage, TId> collectionSettings)
        where TMessage : IMessage;
}

internal interface IMongoDbOutboxCollectionConfigurator
{
    void ConfigureMe(IGetMongoDbOutboxCollectionConfigured configurable);
}

internal sealed class MongoDbOutboxCollectionConfigurator<TMessage, TId>(
    MongoDbPollingCollectionSettings<TMessage, TId>? defaults)
    : IMongoDbOutboxCollectionConfigurator, IMongoDbOutboxCollectionConfigurator<TMessage, TId>
    where TMessage : IMessage
{
    private MongoDbPollingCollectionSettings<TMessage, TId> _settings = defaults ?? new();

    public IMongoDbOutboxCollectionConfigurator<TMessage, TId> WithName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _settings = _settings with { Name = name };
        return this;
    }

    public IMongoDbOutboxCollectionConfigurator<TMessage, TId> WithIdSelector(
        Expression<Func<TMessage, TId>> idSelector)
    {
        ArgumentNullException.ThrowIfNull(idSelector);
        _settings = _settings with { IdSelector = idSelector };
        return this;
    }

    public IMongoDbOutboxCollectionConfigurator<TMessage, TId> WithSort(SortDefinition<TMessage> sort)
    {
        ArgumentNullException.ThrowIfNull(sort);
        _settings = _settings with { Sort = sort };
        return this;
    }

    public IMongoDbOutboxCollectionConfigurator<TMessage, TId> WithProcessedAtSelector(
        Expression<Func<TMessage, DateTime?>> processedAtSelector)
    {
        ArgumentNullException.ThrowIfNull(processedAtSelector);
        _settings = _settings with { ProcessedAtSelector = processedAtSelector };
        return this;
    }

    public void ConfigureMe(IGetMongoDbOutboxCollectionConfigured configurable) =>
        configurable.ConfigureCollection(_settings);
}

internal sealed class PollingOutboxKitConfigurator : IPollingOutboxKitConfigurator, IMongoDbPollingOutboxKitConfigurator
{
    private Func<OutboxKey, IServiceProvider, IMongoDatabase>? _dbFactory;

    private IMongoDbOutboxCollectionConfigurator _collectionConfigurator =
        new MongoDbOutboxCollectionConfigurator<Message, ObjectId>(MongoDbPollingCollectionSettingsDefaults.Defaults);

    private CorePollingSettings _coreSettings = new();
    private MongoDbPollingSettings _pollingSettings = new();
    private MongoDbCleanUpSettings _cleanUpSettings = new();
    private DistributedLockSettings _lockBaseSettings = new() 
    {
        ChangeStreamsEnabled = false
    };
    private MongoDbPollingDistributedLockSettings _lockPollingSettings = new();

    public CorePollingSettings GetCoreSettings() => _coreSettings;

    public IMongoDbPollingOutboxKitConfigurator WithDatabaseFactory(
        Func<OutboxKey, IServiceProvider, IMongoDatabase> dbFactory)
    {
        ArgumentNullException.ThrowIfNull(dbFactory);
        _dbFactory = dbFactory;
        return this;
    }

    public IMongoDbPollingOutboxKitConfigurator WithCollection<TMessage, TId>(
        Action<IMongoDbOutboxCollectionConfigurator<TMessage, TId>> configure) where TMessage : IMessage
    {
        ArgumentNullException.ThrowIfNull(configure);
        var configurator = new MongoDbOutboxCollectionConfigurator<TMessage, TId>(null);
        _collectionConfigurator = configurator;
        configure(configurator);
        return this;
    }

    public IMongoDbPollingOutboxKitConfigurator WithPollingInterval(TimeSpan pollingInterval)
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

    public IMongoDbPollingOutboxKitConfigurator WithBatchSize(int batchSize)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Batch size must be greater than zero");
        }

        _pollingSettings = _pollingSettings with { BatchSize = batchSize };
        return this;
    }

    public IMongoDbPollingOutboxKitConfigurator WithDistributedLock(
        Action<IMongoDbDistributedLockConfigurator> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var cfg = new MongoDbDistributedLockConfigurator();
        configure(cfg);
        _lockBaseSettings = cfg.BaseSettings;
        _lockPollingSettings = cfg.PollingSettings;
        return this;
    }

    public IMongoDbPollingOutboxKitConfigurator WithUpdateProcessed(
        Action<IMongoDbUpdateProcessedConfigurator>? configure)
    {
        var cfg = new MongoDbUpdateProcessedConfigurator();
        configure?.Invoke(cfg);
        _pollingSettings = _pollingSettings with
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
        if (_dbFactory is null) throw new InvalidOperationException("Database factory must be set");

        services.AddKeyedSingleton(key, _dbFactory);

        services.AddKeyedSingleton<DistributedLockThingy>(
            key,
            (s, _) => ActivatorUtilities.CreateInstance<DistributedLockThingy>(
                s,
                _lockBaseSettings,
                s.GetRequiredKeyedService<Func<OutboxKey, IServiceProvider, IMongoDatabase>>(key)(key, s)));

        _collectionConfigurator.ConfigureMe(new GetMongoDbOutboxCollectionConfigured(
            key,
            services,
            _cleanUpSettings,
            _pollingSettings,
            _lockPollingSettings));
    }

    private sealed class GetMongoDbOutboxCollectionConfigured(
        OutboxKey key,
        IServiceCollection services,
        MongoDbCleanUpSettings cleanUpSettings,
        MongoDbPollingSettings pollingSettings,
        MongoDbPollingDistributedLockSettings lockPollingSettings)
        : IGetMongoDbOutboxCollectionConfigured
    {
        public void ConfigureCollection<TMessage, TId>(
            MongoDbPollingCollectionSettings<TMessage, TId> collectionSettings)
            where TMessage : IMessage
        {
            if (string.IsNullOrWhiteSpace(collectionSettings.Name))
                throw new InvalidOperationException("Collection name must be set");
            if (collectionSettings.IdSelector is null) throw new InvalidOperationException("Id selector must be set");
            if (collectionSettings.Sort is null) throw new InvalidOperationException("Sort must be set");
            if (pollingSettings.CompletionMode == CompletionMode.Update
                && collectionSettings.ProcessedAtSelector is null)
                throw new InvalidOperationException("Processed at selector must be set when updating processed");

            if (pollingSettings.CompletionMode == CompletionMode.Update)
            {
                services.AddKeyedSingleton<IOutboxCleaner>(
                    key,
                    (s, _) => ActivatorUtilities.CreateInstance<Cleaner<TMessage>>(
                        s,
                        cleanUpSettings,
                        new MongoDbCleanUpCollectionSettings<TMessage>
                        {
                            Name = collectionSettings.Name,
                            ProcessedAtSelector = collectionSettings.ProcessedAtSelector!
                        },
                        s.GetRequiredKeyedService<Func<OutboxKey, IServiceProvider, IMongoDatabase>>(key)(key, s)));
            }

            services.AddKeyedSingleton(
                key,
                (s, _) => ActivatorUtilities.CreateInstance<OutboxBatchCompleter<TMessage, TId>>(
                    s,
                    key,
                    pollingSettings,
                    collectionSettings,
                    s.GetRequiredKeyedService<Func<OutboxKey, IServiceProvider, IMongoDatabase>>(key)(key, s)));
            
            services.AddKeyedSingleton<IProducedMessagesCompletionRetrier>(
                key,
                (s, _) => s.GetRequiredKeyedService<OutboxBatchCompleter<TMessage, TId>>(key));
            
            services.AddKeyedSingleton<IBatchFetcher>(
                key,
                (s, _) => ActivatorUtilities.CreateInstance<OutboxBatchFetcher<TMessage, TId>>(
                    s,
                    key,
                    pollingSettings,
                    collectionSettings,
                    lockPollingSettings,
                    s.GetRequiredKeyedService<Func<OutboxKey, IServiceProvider, IMongoDatabase>>(key)(key, s),
                    s.GetRequiredKeyedService<DistributedLockThingy>(key)));
        }
    }
}

internal sealed class MongoDbUpdateProcessedConfigurator : IMongoDbUpdateProcessedConfigurator
{
    public TimeSpan CleanUpInterval { get; private set; } = TimeSpan.Zero;
    public TimeSpan MaxAge { get; private set; } = TimeSpan.Zero;

    public IMongoDbUpdateProcessedConfigurator WithCleanUpInterval(TimeSpan cleanUpInterval)
    {
        if (cleanUpInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cleanUpInterval),
                cleanUpInterval,
                "Clean up interval must be greater than zero");
        }

        CleanUpInterval = cleanUpInterval;
        return this;
    }

    public IMongoDbUpdateProcessedConfigurator WithMaxAge(TimeSpan maxAge)
    {
        if (maxAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAge), maxAge, "Max age must be greater than zero");
        }

        MaxAge = maxAge;
        return this;
    }
}

internal sealed class MongoDbDistributedLockConfigurator : IMongoDbDistributedLockConfigurator
{
    public DistributedLockSettings BaseSettings { get; private set; } = new()
    {
        ChangeStreamsEnabled = false
    };

    public MongoDbPollingDistributedLockSettings PollingSettings { get; private set; } = new();

    public IMongoDbDistributedLockConfigurator WithCollectionName(string collectionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);
        BaseSettings = BaseSettings with { CollectionName = collectionName };
        return this;
    }

    public IMongoDbDistributedLockConfigurator WithId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        PollingSettings = PollingSettings with { Id = id };
        return this;
    }

    public IMongoDbDistributedLockConfigurator WithOwner(string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        PollingSettings = PollingSettings with { Owner = owner };
        return this;
    }

    public IMongoDbDistributedLockConfigurator WithChangeStreamsEnabled(bool changeStreamsEnabled)
    {
        BaseSettings = BaseSettings with { ChangeStreamsEnabled = changeStreamsEnabled };
        return this;
    }
}

internal enum CompletionMode
{
    Delete,
    Update
}

internal sealed record MongoDbPollingSettings
{
    public int BatchSize { get; init; } = 100;
    public CompletionMode CompletionMode { get; init; } = CompletionMode.Delete;
}

internal sealed record MongoDbPollingCollectionSettings<TMessage, TId> where TMessage : IMessage
{
    public string Name { get; init; } = "outbox_messages";
    public Expression<Func<TMessage, TId>> IdSelector { get; init; } = null!;
    public SortDefinition<TMessage> Sort { get; init; } = null!;
    public Expression<Func<TMessage, DateTime?>>? ProcessedAtSelector { get; init; }
}

internal static class MongoDbPollingCollectionSettingsDefaults
{
    public static readonly MongoDbPollingCollectionSettings<Message, ObjectId> Defaults = new()
    {
        IdSelector = m => m.Id,
        Sort = Builders<Message>.Sort.Ascending(m => m.Id),
        ProcessedAtSelector = null
    };
}

internal sealed record MongoDbPollingDistributedLockSettings
{
    public string Id { get; init; } = "outbox_lock";
    public string Owner { get; init; } = Environment.MachineName;
}