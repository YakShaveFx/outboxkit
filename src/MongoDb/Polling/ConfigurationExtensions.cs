using System.Linq.Expressions;
using MongoDB.Driver;
using YakShaveFx.OutboxKit.Core;

namespace YakShaveFx.OutboxKit.MongoDb.Polling;

public static class OutboxKitConfiguratorExtensions
{
    /// <summary>
    /// Configures OutboxKit to use MongoDB polling, with a default key.
    /// </summary>
    /// <param name="configurator">The <see cref="IOutboxKitConfigurator"/> to configure MongoDB polling.</param>
    /// <param name="configure">Function to configure OutboxKit with MongoDB polling</param>
    /// <returns>The <see cref="IOutboxKitConfigurator"/> instance for chaining calls.</returns>
    public static IOutboxKitConfigurator WithMongoDbPolling(
        this IOutboxKitConfigurator configurator,
        Action<IMongoDbPollingOutboxKitConfigurator> configure)
    {
        var pollingConfigurator = new PollingOutboxKitConfigurator();
        configure(pollingConfigurator);
        configurator.WithPolling(MongoDbPollingProvider.DefaultKey, pollingConfigurator);
        return configurator;
    }

    /// <summary>
    /// Configures OutboxKit to use MongoDB polling, with a given key.
    /// </summary>
    /// <param name="configurator">The <see cref="IOutboxKitConfigurator"/> to configure MongoDB polling.</param>
    /// <param name="key">The key assigned to the outbox instance being configured.</param>
    /// <param name="configure">Function to configure OutboxKit with MongoDB polling</param>
    /// <returns>The <see cref="IOutboxKitConfigurator"/> instance for chaining calls.</returns>
    public static IOutboxKitConfigurator WithMongoDbPolling(
        this IOutboxKitConfigurator configurator,
        string key,
        Action<IMongoDbPollingOutboxKitConfigurator> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(key));
        var pollingConfigurator = new PollingOutboxKitConfigurator();
        configure(pollingConfigurator);
        configurator.WithPolling(MongoDbPollingProvider.CreateKey(key), pollingConfigurator);
        return configurator;
    }
}

/// <summary>
/// Allows configuring the MongoDB polling outbox.
/// </summary>
public interface IMongoDbPollingOutboxKitConfigurator
{
    /// <summary>
    /// Configures the factory to create the MongoDB database.
    /// </summary>
    /// <param name="databaseFactory">The factory to create the MongoDB database.</param>
    /// <returns>The <see cref="IMongoDbPollingOutboxKitConfigurator"/> instance for chaining calls.</returns>
    IMongoDbPollingOutboxKitConfigurator WithDatabaseFactory(Func<OutboxKey, IServiceProvider, IMongoDatabase> databaseFactory);

    /// <summary>
    /// Configures the outbox collection.
    /// </summary>
    /// <param name="configure">A function to configure the outbox collection.</param>
    /// <returns>The <see cref="IMongoDbPollingOutboxKitConfigurator"/> instance for chaining calls.</returns>
    IMongoDbPollingOutboxKitConfigurator WithCollection<TMessage, TId>(
        Action<IMongoDbOutboxCollectionConfigurator<TMessage, TId>> configure)
        where TMessage : IMessage;

    /// <summary>
    /// Configures the outbox polling interval.
    /// </summary>
    /// <param name="pollingInterval">The interval at which the outbox is polled for new messages.</param>
    /// <returns>The <see cref="IMongoDbPollingOutboxKitConfigurator"/> instance for chaining calls.</returns>
    IMongoDbPollingOutboxKitConfigurator WithPollingInterval(TimeSpan pollingInterval);

    /// <summary>
    /// Configures the amount of messages to fetch from the outbox in each batch.
    /// </summary>
    /// <param name="batchSize">The amount of messages to fetch from the outbox in each batch.</param>
    /// <returns>The <see cref="IMongoDbPollingOutboxKitConfigurator"/> instance for chaining calls.</returns>
    IMongoDbPollingOutboxKitConfigurator WithBatchSize(int batchSize);

    /// <summary>
    /// Configures the distributed lock used by OutboxKit to prevent multiple instances from processing the same messages.
    /// </summary>
    /// <param name="configure">A function to configure the distributed lock.</param>
    /// <returns>The <see cref="IMongoDbPollingOutboxKitConfigurator"/> instance for chaining calls.</returns>
    IMongoDbPollingOutboxKitConfigurator WithDistributedLock(Action<IMongoDbDistributedLockConfigurator> configure);

    /// <summary>
    /// Configures the outbox to update processed messages, instead of deleting them.
    /// </summary>
    /// <param name="configure">A function to configure updating processed messages.</param>
    /// <returns>The <see cref="IMongoDbPollingOutboxKitConfigurator"/> instance for chaining calls.</returns>
    /// <remarks>OutboxKit assumes the "processed at" property is a (nullable) <see cref="DateTime"/> in UTC,
    /// and uses <see cref="TimeProvider"/> to obtain the time when completing and cleaning up the messages.</remarks>
    IMongoDbPollingOutboxKitConfigurator WithUpdateProcessed(Action<IMongoDbUpdateProcessedConfigurator>? configure);
}

/// <summary>
/// Allows configuring the outbox collection.
/// </summary>
/// <typeparam name="TMessage">The type of the message in the outbox.</typeparam>
/// <typeparam name="TId">The type of the id of the message in the outbox.</typeparam>
public interface IMongoDbOutboxCollectionConfigurator<TMessage, TId> where TMessage : IMessage
{
    /// <summary>
    /// Configures the name of the outbox collection.
    /// </summary>
    /// <param name="name">The name of the outbox collection.</param>
    /// <returns>The <see cref="IMongoDbOutboxCollectionConfigurator{TMessage, TId}"/> instance for chaining calls.</returns>
    IMongoDbOutboxCollectionConfigurator<TMessage, TId> WithName(string name);
    
    /// <summary>
    /// Configures the selector for the id of the message in the outbox.
    /// </summary>
    /// <param name="idSelector">The selector for the id of the message in the outbox.</param>
    /// <returns>The <see cref="IMongoDbOutboxCollectionConfigurator{TMessage, TId}"/> instance for chaining calls.</returns>
    IMongoDbOutboxCollectionConfigurator<TMessage, TId> WithIdSelector(Expression<Func<TMessage, TId>> idSelector);
    
    /// <summary>
    /// Configures the sort definition used when fetching messages from outbox collection.
    /// </summary>
    /// <param name="sort">The sort definition used when fetching messages from outbox collection.</param>
    /// <returns>The <see cref="IMongoDbOutboxCollectionConfigurator{TMessage, TId}"/> instance for chaining calls.</returns>
    IMongoDbOutboxCollectionConfigurator<TMessage, TId> WithSort(SortDefinition<TMessage> sort);

    /// <summary>
    /// Configures the selector for the "processed at" property of the message in the outbox.
    /// </summary>
    /// <param name="processedAtSelector">The selector for the "processed at" property of the message in the outbox.</param>
    /// <returns>The <see cref="IMongoDbOutboxCollectionConfigurator{TMessage, TId}"/> instance for chaining calls.</returns>
    /// <remarks>
    /// Only required when the processed messages are updated instead of deleted.
    /// </remarks>
    IMongoDbOutboxCollectionConfigurator<TMessage, TId> WithProcessedAtSelector(
        Expression<Func<TMessage, DateTime?>> processedAtSelector);
}

/// <summary>
/// Allows configuring the distributed lock used by OutboxKit to prevent multiple instances from processing the same messages.
/// </summary>
public interface IMongoDbDistributedLockConfigurator
{
    /// <summary>
    /// Configures the collection name where the distributed lock is stored (defaults to outbox_locks).
    /// </summary>
    /// <param name="collectionName">The collection name where the distributed lock is stored.</param>
    /// <returns>The <see cref="IMongoDbDistributedLockConfigurator"/> instance for chaining calls.</returns>
    IMongoDbDistributedLockConfigurator WithCollectionName(string collectionName);

    /// <summary>
    /// Configures the id of the distributed lock (defaults to outbox_lock).
    /// </summary>
    /// <param name="id">The id of the distributed lock.</param>
    /// <returns>The <see cref="IMongoDbDistributedLockConfigurator"/> instance for chaining calls.</returns>
    /// <remarks>
    /// The id should be the same for all instances of the application that share the same outbox.
    /// </remarks>
    IMongoDbDistributedLockConfigurator WithId(string id);

    /// <summary>
    /// Configures the owner of the distributed lock (defaults to <code>Environment.MachineName</code>).
    /// </summary>
    /// <param name="owner">The owner of the distributed lock.</param>
    /// <returns>The <see cref="IMongoDbDistributedLockConfigurator"/> instance for chaining calls.</returns>
    /// <remarks>
    /// The owner should be unique for each instance of the application that shares the same outbox.
    /// </remarks>
    IMongoDbDistributedLockConfigurator WithOwner(string owner);

    /// <summary>
    /// Configures whether change streams are enabled for the distributed lock (defaults to false).
    /// </summary>
    /// <param name="changeStreamsEnabled">Whether change streams are enabled for the distributed lock.</param>
    /// <returns>The <see cref="IMongoDbDistributedLockConfigurator"/> instance for chaining calls.</returns>
    IMongoDbDistributedLockConfigurator WithChangeStreamsEnabled(bool changeStreamsEnabled);
}

/// <summary>
/// Allows configuring the outbox to update processed messages, instead of deleting them.
/// </summary>
public interface IMongoDbUpdateProcessedConfigurator
{
    /// <summary>
    /// Configures the interval at which processed messages are cleaned up.
    /// </summary>
    /// <param name="cleanUpInterval">The interval at which processed messages are cleaned up.</param>
    /// <returns>The <see cref="IMongoDbUpdateProcessedConfigurator"/> instance for chaining calls.</returns>
    IMongoDbUpdateProcessedConfigurator WithCleanUpInterval(TimeSpan cleanUpInterval);

    /// <summary>
    /// Configures the amount of time after which processed messages should be deleted.
    /// </summary>
    /// <param name="maxAge">The amount of time after which processed messages should be deleted.</param>
    /// <returns>The <see cref="IMongoDbUpdateProcessedConfigurator"/> instance for chaining calls.</returns>
    IMongoDbUpdateProcessedConfigurator WithMaxAge(TimeSpan maxAge);
}
