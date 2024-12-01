using MySqlConnector;
using YakShaveFx.OutboxKit.Core;

namespace YakShaveFx.OutboxKit.MySql.Shared;

/// <summary>
/// Allows configuring the outbox table.
/// </summary>
public interface IMySqlOutboxTableConfigurator
{
    /// <summary>
    /// Configures the table name.
    /// </summary>
    /// <param name="name">The table name.</param>
    /// <returns>The <see cref="IMySqlOutboxTableConfigurator"/> instance for chaining calls.</returns>
    IMySqlOutboxTableConfigurator WithName(string name);

    /// <summary>
    /// Configures the columns of the table that should be fetched when polling the outbox.
    /// </summary>
    /// <param name="columns"></param>
    /// <returns>The <see cref="IMySqlOutboxTableConfigurator"/> instance for chaining calls.</returns>
    IMySqlOutboxTableConfigurator WithColumnSelection(IReadOnlyCollection<string> columns);

    /// <summary>
    /// Configures the column that is used as the id.
    /// </summary>
    /// <param name="column">The column that is used as the id.</param>
    /// <returns>The <see cref="IMySqlOutboxTableConfigurator"/> instance for chaining calls.</returns>
    IMySqlOutboxTableConfigurator WithIdColumn(string column);

    /// <summary>
    /// Configures the order in which messages are fetched from the outbox.
    /// </summary>
    /// <param name="sortExpressions">The expressions that define the order in which messages are fetched from the outbox.</param>
    /// <returns>The <see cref="IMySqlOutboxTableConfigurator"/> instance for chaining calls.</returns>
    IMySqlOutboxTableConfigurator WithSorting(IReadOnlyCollection<SortExpression> sortExpressions);

    /// <summary>
    /// Configures the column that indicates when a message was processed.
    /// </summary>
    /// <param name="column">The column that indicates when a message was processed.</param>
    /// <returns>The <see cref="IMySqlOutboxTableConfigurator"/> instance for chaining calls.</returns>
    /// <remarks>Only used when outbox is configured to update processed messages, instead of deleting them.</remarks>
    IMySqlOutboxTableConfigurator WithProcessedAtColumn(string column);

    /// <summary>
    /// Configures a function to get the id from a message.
    /// </summary>
    /// <param name="idGetter"></param>
    /// <returns>The <see cref="IMySqlOutboxTableConfigurator"/> instance for chaining calls.</returns>
    IMySqlOutboxTableConfigurator WithIdGetter(Func<IMessage, object> idGetter);

    /// <summary>
    /// Configures a function to create a message from a MySql data reader.
    /// </summary>
    /// <param name="messageFactory">A <see cref="MySqlDataReader"/> to read an <see cref="IMessage"/> from.</param>
    /// <returns>The <see cref="IMySqlOutboxTableConfigurator"/> instance for chaining calls.</returns>
    /// <remarks>To fetch a column by its index (i.e. using <see cref="MySqlDataReader.GetFieldValue{T}"/>) the index of each column matches that of <see cref="WithColumnSelection"/>.</remarks>
    IMySqlOutboxTableConfigurator WithMessageFactory(Func<MySqlDataReader, IMessage> messageFactory);
}

internal sealed class MySqlOutboxTableConfigurator : IMySqlOutboxTableConfigurator
{
    private string _tableName = TableConfiguration.Default.Name;

    private IReadOnlyCollection<string> _columns = TableConfiguration.Default.Columns;
    private string _idColumn = TableConfiguration.Default.IdColumn;
    private IReadOnlyCollection<SortExpression> _sortExpressions = TableConfiguration.Default.SortExpressions;
    private string _processedAtColumn = TableConfiguration.Default.ProcessedAtColumn;
    private Func<IMessage, object> _idGetter = TableConfiguration.Default.IdGetter;
    private Func<MySqlDataReader, IMessage> _messageFactory = TableConfiguration.Default.MessageFactory;

    public TableConfiguration BuildConfiguration() => new(
        _tableName,
        _columns,
        _idColumn,
        _sortExpressions,
        _processedAtColumn,
        _idGetter,
        _messageFactory);

    public IMySqlOutboxTableConfigurator WithName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));
        _tableName = name;
        return this;
    }

    public IMySqlOutboxTableConfigurator WithColumnSelection(IReadOnlyCollection<string> columns)
    {
        if (columns is not { Count: > 0 })
        {
            throw new ArgumentException("Column names must not be empty", nameof(columns));
        }

        _columns = columns.Select(c => c.Trim()).ToArray();
        return this;
    }

    public IMySqlOutboxTableConfigurator WithIdColumn(string column)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column, nameof(column));
        _idColumn = column.Trim();
        return this;
    }

    public IMySqlOutboxTableConfigurator WithSorting(IReadOnlyCollection<SortExpression> sortExpressions)
    {
        if (sortExpressions is not { Count: > 0 })
        {
            throw new ArgumentException("Sort expressions must not be empty", nameof(sortExpressions));
        }

        _sortExpressions = sortExpressions;
        return this;
    }

    public IMySqlOutboxTableConfigurator WithProcessedAtColumn(string column)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column, nameof(column));
        _processedAtColumn = column.Trim();
        return this;
    }

    public IMySqlOutboxTableConfigurator WithIdGetter(Func<IMessage, object> idGetter)
    {
        ArgumentNullException.ThrowIfNull(idGetter, nameof(idGetter));
        _idGetter = idGetter;
        return this;
    }

    public IMySqlOutboxTableConfigurator WithMessageFactory(Func<MySqlDataReader, IMessage> messageFactory)
    {
        ArgumentNullException.ThrowIfNull(messageFactory, nameof(messageFactory));
        _messageFactory = messageFactory;
        return this;
    }
}