using System.Text.Json;
using Dapper;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit.Sdk;
using Xunit.v3;
using YakShaveFx.OutboxKit.PostgreSql.Tests;

[assembly: AssemblyFixture(typeof(PostgreSqlFixture))]

namespace YakShaveFx.OutboxKit.PostgreSql.Tests;

// ReSharper disable once ClassNeverInstantiated.Global - it's instantiated by xUnit
public sealed class PostgreSqlFixture(IMessageSink diagnosticMessageSink) : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithUsername("user")
        .WithPassword("pass")
        .Build();

    public IDatabaseContextInitializer DbInit
        => new DatabaseContextInitializer(_container.GetConnectionString(), diagnosticMessageSink);

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    private sealed class DatabaseContextInitializer(string originalConnectionString, IMessageSink diagnosticMessageSink)
        : IDatabaseContextInitializer
    {
        private DefaultSchemaSettings _settings = Defaults.Delete.DefaultSchemaSettings;

        // int for now, maybe we'll need a more complex seeding strategy later
        private int _seedCount;

        public IDatabaseContextInitializer WithDefaultSchema(DefaultSchemaSettings settings)
        {
            _settings = settings;
            return this;
        }

        public IDatabaseContextInitializer WithSeed(int count = 10)
        {
            _seedCount = count;
            return this;
        }

        public async Task<IDatabaseContext> InitAsync()
        {
            var databaseName = $"test_{Guid.NewGuid():N}";
            await using (var createDbConn = new NpgsqlConnection(originalConnectionString))
            {
                await createDbConn.OpenAsync();
                await using var createDbCommand = new NpgsqlCommand($"CREATE DATABASE {databaseName};", createDbConn);
                await createDbCommand.ExecuteNonQueryAsync();
            }

            await using var connection = new NpgsqlConnection(
                new NpgsqlConnectionStringBuilder(originalConnectionString)
                {
                    Database = databaseName,
                }.ConnectionString);
            
            await connection.OpenAsync();
            
            if (_settings.WithProcessedAtColumn)
            {
                await connection.SetupDatabaseWithDefaultWithProcessedAtAsync();
            }
            else
            {
                await connection.SetupDatabaseWithDefaultSettingsAsync();
            }

            if (_seedCount > 0)
            {
                await connection.SeedAsync(databaseName, _seedCount);
            }

            return new DatabaseContext(originalConnectionString, databaseName, diagnosticMessageSink);
        }
    }

    private sealed class DatabaseContext(
        string originalConnectionString,
        string databaseName,
        IMessageSink diagnosticMessageSink) : IDatabaseContext
    {
        public NpgsqlDataSource DataSource { get; } = new NpgsqlDataSourceBuilder(
            new NpgsqlConnectionStringBuilder(originalConnectionString)
            {
                Database = databaseName,
            }.ConnectionString).Build();

        public string DatabaseName => databaseName;

        public async ValueTask DisposeAsync()
        {
            await DataSource.DisposeAsync();
            try
            {
                await using var c = new NpgsqlConnection(originalConnectionString);
                await c.OpenAsync();
                var command = new NpgsqlCommand($"DROP DATABASE {databaseName};", c);
                await command.ExecuteNonQueryAsync();
            }
            catch (Exception e)
            {
                diagnosticMessageSink.OnMessage(
                    new DiagnosticMessage($"Failed to drop database {databaseName}: {e.Message}"));
            }
        }
    }
}

public interface IDatabaseContext : IAsyncDisposable
{
    NpgsqlDataSource DataSource { get; }
    string DatabaseName { get; }
}

public record DefaultSchemaSettings
{
    public bool WithProcessedAtColumn { get; init; }
}

public interface IDatabaseContextInitializer
{
    IDatabaseContextInitializer WithDefaultSchema(DefaultSchemaSettings settings);

    IDatabaseContextInitializer WithSeed(int seedCount = 10);

    Task<IDatabaseContext> InitAsync();
}

file static class InitializationExtensions
{
    public static async Task SetupDatabaseWithDefaultSettingsAsync(this NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            // lang=postgresql
            """
             create table if not exists outbox_messages
             (
                 id              bigint generated always as identity primary key,
                 type            varchar(128) not null,
                 payload         bytea         not null,
                 created_at      timestamp(6)  not null,
                 trace_context   bytea         null
             );
             """, connection);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task SetupDatabaseWithDefaultWithProcessedAtAsync(this NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            // lang=postgresql
            """
            create table if not exists outbox_messages
            (
                id              bigint generated always as identity primary key,
                type            varchar(128)  not null,
                payload         bytea         not null,
                created_at      timestamp(6)  not null,
                trace_context   bytea         null,
                processed_at    timestamp(6)  null
            );
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task SeedAsync(this NpgsqlConnection connection, string databaseName, int seedCount)
    {
        if (seedCount == 0) return;

        var messages = Enumerable.Range(1, seedCount).Select(i => new Message
        {
            Type = "some-type",
            Payload = JsonSerializer.SerializeToUtf8Bytes($"payload{i}"),
            CreatedAt = DateTime.UtcNow,
            TraceContext = null
        });

        await connection.ExecuteAsync(
            // lang=postgresql
            $"INSERT INTO outbox_messages (type, payload, created_at, trace_context) VALUES (@Type, @Payload, @CreatedAt, @TraceContext);",
            messages);
    }
}