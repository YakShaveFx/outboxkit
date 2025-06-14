using Npgsql;
using YakShaveFx.OutboxKit.Core;

namespace YakShaveFx.OutboxKit.PostgreSql.Shared;

/// <summary>
/// Allows configuring the outbox table.
/// </summary>
public interface IPostgreSqlOutboxTableConfigurator
{
    /// <summary>
    /// Configures the table name.
    /// </summary>
    /// <param name="name">The table name.</param>
    /// <returns>The <see cref="IPostgreSqlOutboxTableConfigurator"/> instance for chaining calls.</returns>
    IPostgreSqlOutboxTableConfigurator WithName(string name);

    /// <summary>
    /// Configures the columns of the table that should be fetched when polling the outbox.
    /// </summary>
    /// <param name="columns"></param>
    /// <returns>The <see cref="IPostgreSqlOutboxTableConfigurator"/> instance for chaining calls.</returns>
    IPostgreSqlOutboxTableConfigurator WithColumnSelection(IReadOnlyCollection<string> columns);

    /// <summary>
    /// Configures the column that is used as the id.
    /// </summary>
    /// <param name="column">The column that is used as the id.</param>
    /// <returns>The <see cref="IPostgreSqlOutboxTableConfigurator"/> instance for chaining calls.</returns>
    IPostgreSqlOutboxTableConfigurator WithIdColumn(string column);

    /// <summary>
    /// Configures the order in which messages are fetched from the outbox.
    /// </summary>
    /// <param name="sortExpressions">The expressions that define the order in which messages are fetched from the outbox.</param>
    /// <returns>The <see cref="IPostgreSqlOutboxTableConfigurator"/> instance for chaining calls.</returns>
    IPostgreSqlOutboxTableConfigurator WithSorting(IReadOnlyCollection<SortExpression> sortExpressions);

    /// <summary>
    /// Configures the column that indicates when a message was processed.
    /// </summary>
    /// <param name="column">The column that indicates when a message was processed.</param>
    /// <returns>The <see cref="IPostgreSqlOutboxTableConfigurator"/> instance for chaining calls.</returns>
    /// <remarks>Only used when outbox is configured to update processed messages, instead of deleting them.</remarks>
    IPostgreSqlOutboxTableConfigurator WithProcessedAtColumn(string column);

    /// <summary>
    /// Configures a function to get the id from a message.
    /// </summary>
    /// <param name="idGetter"></param>
    /// <returns>The <see cref="IPostgreSqlOutboxTableConfigurator"/> instance for chaining calls.</returns>
    IPostgreSqlOutboxTableConfigurator WithIdGetter(Func<IMessage, object> idGetter);

    /// <summary>
    /// Configures a function to create a message from a PostgreSql data reader.
    /// </summary>
    /// <param name="messageFactory">A <see cref="PostgreSqlDataReader"/> to read an <see cref="IMessage"/> from.</param>
    /// <returns>The <see cref="IPostgreSqlOutboxTableConfigurator"/> instance for chaining calls.</returns>
    /// <remarks>To fetch a column by its index (i.e. using <see cref="PostgreSqlDataReader.GetFieldValue{T}"/>) the index of each column matches that of <see cref="WithColumnSelection"/>.</remarks>
    IPostgreSqlOutboxTableConfigurator WithMessageFactory(Func<NpgsqlDataReader, IMessage> messageFactory);
}

internal sealed class PostgreSqlOutboxTableConfigurator : IPostgreSqlOutboxTableConfigurator
{
    private string _tableName = TableConfiguration.Default.Name;

    private IReadOnlyCollection<string> _columns = TableConfiguration.Default.Columns;
    private string _idColumn = TableConfiguration.Default.IdColumn;
    private IReadOnlyCollection<SortExpression> _sortExpressions = TableConfiguration.Default.SortExpressions;
    private string _processedAtColumn = TableConfiguration.Default.ProcessedAtColumn;
    private Func<IMessage, object> _idGetter = TableConfiguration.Default.IdGetter;
    private Func<NpgsqlDataReader, IMessage> _messageFactory = TableConfiguration.Default.MessageFactory;

    public TableConfiguration BuildConfiguration() => new(
        _tableName,
        _columns,
        _idColumn,
        _sortExpressions,
        _processedAtColumn,
        _idGetter,
        _messageFactory);

    public IPostgreSqlOutboxTableConfigurator WithName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));
        _tableName = name;
        return this;
    }

    public IPostgreSqlOutboxTableConfigurator WithColumnSelection(IReadOnlyCollection<string> columns)
    {
        if (columns is not { Count: > 0 })
        {
            throw new ArgumentException("Column names must not be empty", nameof(columns));
        }

        _columns = columns.Select(c => c.Trim()).ToArray();
        return this;
    }

    public IPostgreSqlOutboxTableConfigurator WithIdColumn(string column)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column, nameof(column));
        _idColumn = column.Trim();
        return this;
    }

    public IPostgreSqlOutboxTableConfigurator WithSorting(IReadOnlyCollection<SortExpression> sortExpressions)
    {
        if (sortExpressions is not { Count: > 0 })
        {
            throw new ArgumentException("Sort expressions must not be empty", nameof(sortExpressions));
        }

        _sortExpressions = sortExpressions.Select(se => se with { Column = se.Column.Trim() }).ToArray();
        return this;
    }

    public IPostgreSqlOutboxTableConfigurator WithProcessedAtColumn(string column)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column, nameof(column));
        _processedAtColumn = column.Trim();
        return this;
    }

    public IPostgreSqlOutboxTableConfigurator WithIdGetter(Func<IMessage, object> idGetter)
    {
        ArgumentNullException.ThrowIfNull(idGetter, nameof(idGetter));
        _idGetter = idGetter;
        return this;
    }

    public IPostgreSqlOutboxTableConfigurator WithMessageFactory(Func<NpgsqlDataReader, IMessage> messageFactory)
    {
        ArgumentNullException.ThrowIfNull(messageFactory, nameof(messageFactory));
        _messageFactory = messageFactory;
        return this;
    }
}