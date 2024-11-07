using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using YakShaveFx.OutboxKit.Core;
using YakShaveFx.OutboxKit.Core.Polling;

namespace YakShaveFx.OutboxKit.MySql.Polling;

public static class OutboxKitConfiguratorExtensions
{
    public static IOutboxKitConfigurator WithMySqlPolling(
        this IOutboxKitConfigurator configurator,
        Action<IMySqlPollingOutboxKitConfigurator> configure)
    {
        var pollingConfigurator = new PollingOutboxKitConfigurator();
        configure(pollingConfigurator);
        configurator.WithPolling(pollingConfigurator);
        return configurator;
    }

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

public interface IMySqlPollingOutboxKitConfigurator
{
    IMySqlPollingOutboxKitConfigurator WithConnectionString(string connectionString);

    IMySqlPollingOutboxKitConfigurator WithTable(Action<IMySqlPollingOutboxTableConfigurator> configure);

    IMySqlPollingOutboxKitConfigurator WithPollingInterval(TimeSpan pollingInterval);

    IMySqlPollingOutboxKitConfigurator WithBatchSize(int batchSize);
}

public interface IMySqlPollingOutboxTableConfigurator
{
    IMySqlPollingOutboxTableConfigurator WithName(string name);

    IMySqlPollingOutboxTableConfigurator WithColumns(IReadOnlyCollection<string> columns);

    IMySqlPollingOutboxTableConfigurator WithIdColumn(string column);

    IMySqlPollingOutboxTableConfigurator WithOrderByColumn(string column);

    IMySqlPollingOutboxTableConfigurator WithIdGetter(Func<IMessage, object> idGetter);

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
            "observability_context"
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
            ObservabilityContext = r.IsDBNull(4) ? null : r.GetFieldValue<byte[]>(4)
        });
}

internal sealed record MySqlPollingSettings
{
    public int BatchSize { get; init; } = 100;
}