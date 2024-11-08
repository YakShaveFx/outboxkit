using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using YakShaveFx.OutboxKit.Core;
using YakShaveFx.OutboxKit.Core.Polling;

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
        configurator.WithPolling(pollingConfigurator);
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
        configurator.WithPolling(key, pollingConfigurator);
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
    IMySqlPollingOutboxKitConfigurator WithTable(Action<IMySqlPollingOutboxTableConfigurator> configure);

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
}

/// <summary>
/// Allows configuring the outbox table.
/// </summary>
public interface IMySqlPollingOutboxTableConfigurator
{
    /// <summary>
    /// Configures the table name.
    /// </summary>
    /// <param name="name">The table name.</param>
    /// <returns>The <see cref="IMySqlPollingOutboxTableConfigurator"/> instance for chaining calls.</returns>
    IMySqlPollingOutboxTableConfigurator WithName(string name);

    /// <summary>
    /// Configures the columns of the table, that should be fetched when polling the outbox.
    /// </summary>
    /// <param name="columns"></param>
    /// <returns>The <see cref="IMySqlPollingOutboxTableConfigurator"/> instance for chaining calls.</returns>
    IMySqlPollingOutboxTableConfigurator WithColumns(IReadOnlyCollection<string> columns);

    /// <summary>
    /// Configures the column that is used as the id.
    /// </summary>
    /// <param name="column"></param>
    /// <returns>The <see cref="IMySqlPollingOutboxTableConfigurator"/> instance for chaining calls.</returns>
    IMySqlPollingOutboxTableConfigurator WithIdColumn(string column);

    /// <summary>
    /// Configures the column that is used to order the messages.
    /// </summary>
    /// <param name="column"></param>
    /// <returns>The <see cref="IMySqlPollingOutboxTableConfigurator"/> instance for chaining calls.</returns>
    IMySqlPollingOutboxTableConfigurator WithOrderByColumn(string column);

    /// <summary>
    /// Configures a function to get the id from a message.
    /// </summary>
    /// <param name="idGetter"></param>
    /// <returns>The <see cref="IMySqlPollingOutboxTableConfigurator"/> instance for chaining calls.</returns>
    IMySqlPollingOutboxTableConfigurator WithIdGetter(Func<IMessage, object> idGetter);

    /// <summary>
    /// Configures a function to create a message from a MySql data reader.
    /// </summary>
    /// <param name="messageFactory"></param>
    /// <returns>The <see cref="IMySqlPollingOutboxTableConfigurator"/> instance for chaining calls.</returns>
    IMySqlPollingOutboxTableConfigurator WithMessageFactory(Func<MySqlDataReader, IMessage> messageFactory);
}

internal sealed class PollingOutboxKitConfigurator : IPollingOutboxKitConfigurator, IMySqlPollingOutboxKitConfigurator
{
    private readonly MySqlPollingOutboxTableConfigurator _tableConfigurator = new();
    private string? _connectionString;
    private CorePollingSettings _corePollingSettings = new();
    private MySqlPollingSettings _mySqlPollingSettings = new();

    public IMySqlPollingOutboxKitConfigurator WithConnectionString(string connectionString)
    {
        _connectionString = connectionString;
        return this;
    }

    public IMySqlPollingOutboxKitConfigurator WithTable(Action<IMySqlPollingOutboxTableConfigurator> configure)
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

        _corePollingSettings = _corePollingSettings with { PollingInterval = pollingInterval };
        return this;
    }

    public IMySqlPollingOutboxKitConfigurator WithBatchSize(int batchSize)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Batch size must be greater than zero");
        }

        _mySqlPollingSettings = _mySqlPollingSettings with { BatchSize = batchSize };
        return this;
    }

    public void ConfigureServices(string key, IServiceCollection services)
    {
        if (_connectionString is null)
        {
            throw new InvalidOperationException($"Connection string must be set for MySql polling with key \"{key}\"");
        }

        services
            .AddKeyedMySqlDataSource(key, _connectionString)
            .AddKeyedSingleton<IOutboxBatchFetcher>(
                key,
                (s, _) => new OutboxBatchFetcher(
                    _mySqlPollingSettings,
                    _tableConfigurator.BuildConfiguration(),
                    s.GetRequiredKeyedService<MySqlDataSource>(key)));
    }

    public CorePollingSettings GetCoreSettings() => _corePollingSettings;
}

internal sealed class MySqlPollingOutboxTableConfigurator : IMySqlPollingOutboxTableConfigurator
{
    private string _tableName = TableConfiguration.Default.Name;

    private IReadOnlyCollection<string> _columns = TableConfiguration.Default.Columns;
    private string _idColumn = TableConfiguration.Default.IdColumn;
    private string _orderByColumn = TableConfiguration.Default.OrderByColumn;
    private Func<IMessage, object> _idGetter = TableConfiguration.Default.IdGetter;
    private Func<MySqlDataReader, IMessage> _messageFactory = TableConfiguration.Default.MessageFactory;

    public TableConfiguration BuildConfiguration() => new(
        _tableName,
        _columns,
        _idColumn,
        _orderByColumn,
        _idGetter, _messageFactory);

    public IMySqlPollingOutboxTableConfigurator WithName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));
        _tableName = name;
        return this;
    }

    public IMySqlPollingOutboxTableConfigurator WithColumns(IReadOnlyCollection<string> columns)
    {
        if (columns is not { Count: > 0 })
        {
            throw new ArgumentException("Column names must not be empty", nameof(columns));
        }

        _columns = columns.Select(c => c.Trim()).ToArray();
        return this;
    }

    public IMySqlPollingOutboxTableConfigurator WithIdColumn(string column)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column, nameof(column));
        _idColumn = column.Trim();
        return this;
    }

    public IMySqlPollingOutboxTableConfigurator WithOrderByColumn(string column)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column, nameof(column));
        _orderByColumn = column.Trim();
        return this;
    }

    public IMySqlPollingOutboxTableConfigurator WithIdGetter(Func<IMessage, object> idGetter)
    {
        ArgumentNullException.ThrowIfNull(idGetter, nameof(idGetter));
        _idGetter = idGetter;
        return this;
    }

    public IMySqlPollingOutboxTableConfigurator WithMessageFactory(Func<MySqlDataReader, IMessage> messageFactory)
    {
        ArgumentNullException.ThrowIfNull(messageFactory, nameof(messageFactory));
        _messageFactory = messageFactory;
        return this;
    }
}

internal sealed record TableConfiguration(
    string Name,
    IReadOnlyCollection<string> Columns,
    string IdColumn,
    string OrderByColumn,
    Func<IMessage, object> IdGetter,
    Func<MySqlDataReader, IMessage> MessageFactory)
{
    public static TableConfiguration Default { get; } = new(
        "outbox_messages",
        [
            "id",
            "type",
            "payload",
            "created_at",
            "trace_context"
        ],
        "id",
        "id",
        m => ((Message)m).Id,
        r => new Message
        {
            Id = r.GetInt64(0),
            Type = r.GetString(1),
            Payload = r.GetFieldValue<byte[]>(2),
            CreatedAt = r.GetDateTime(3),
            TraceContext = r.IsDBNull(4) ? null : r.GetFieldValue<byte[]>(4)
        });
}

internal sealed record MySqlPollingSettings
{
    public int BatchSize { get; init; } = 100;
}