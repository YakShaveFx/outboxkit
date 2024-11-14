using Dapper;
using MySqlConnector;
using Testcontainers.MySql;
using Xunit.Abstractions;
using Xunit.Sdk;
using System.Text.Json;
using YakShaveFx.OutboxKit.MySql.Polling;
using YakShaveFx.OutboxKit.MySql.Tests.Polling;

namespace YakShaveFx.OutboxKit.MySql.Tests;

// in xunit 3, we'll be able to use assembly fixtures to share the container across all tests
// until then, we'll have to use a collection fixture (though this means the tests don't run in parallel)

[CollectionDefinition(Name)]
public sealed class MySqlCollection : ICollectionFixture<MySqlFixture>
{
    public const string Name = "MySQL collection";

    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}

// ReSharper disable once ClassNeverInstantiated.Global - it's instantiated by xUnit
public sealed class MySqlFixture(IMessageSink diagnosticMessageSink) : IAsyncLifetime
{
    private readonly MySqlContainer _container = new MySqlBuilder()
        .WithImage("mysql:8.0")
        .WithUsername("root")
        .WithPassword("root")
        .Build();

    public IDatabaseContextInitializer DbInit
        => new DatabaseContextInitializer(_container.GetConnectionString(), diagnosticMessageSink);

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

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
            await using var connection = new MySqlConnection(originalConnectionString);
            await connection.OpenAsync();
            await using var createDbCommand = new MySqlCommand($"CREATE DATABASE {databaseName};", connection);
            await createDbCommand.ExecuteNonQueryAsync();

            if (_settings.WithProcessedAtColumn)
            {
                await connection.SetupDatabaseWithDefaultWithProcessedAtAsync(databaseName);
            }
            else
            {
                await connection.SetupDatabaseWithDefaultSettingsAsync(databaseName);
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
        public MySqlDataSource DataSource { get; } = new(new MySqlConnectionStringBuilder(originalConnectionString)
        {
            Database = databaseName,
        }.ConnectionString);

        public async ValueTask DisposeAsync()
        {
            await DataSource.DisposeAsync();
            try
            {
                await using var c = new MySqlConnection(originalConnectionString);
                await c.OpenAsync();
                var command = new MySqlCommand($"DROP DATABASE {databaseName};", c);
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
    MySqlDataSource DataSource { get; }
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
    public static async Task SetupDatabaseWithDefaultSettingsAsync(this MySqlConnection connection, string databaseName)
    {
        await using var command = new MySqlCommand(
            // lang=mysql
            $"""
             create table if not exists {databaseName}.outbox_messages
             (
                 id                    bigint auto_increment primary key,
                 type                  varchar(128) not null,
                 payload               longblob     not null,
                 created_at            datetime(6)  not null,
                 trace_context         longblob     null
             );
             """, connection);
        await command.ExecuteNonQueryAsync();
    }
    
    public static async Task SetupDatabaseWithDefaultWithProcessedAtAsync(this MySqlConnection connection, string databaseName)
    {
        await using var command = new MySqlCommand(
            // lang=mysql
            $"""
             create table if not exists {databaseName}.outbox_messages
             (
                 id                    bigint auto_increment primary key,
                 type                  varchar(128) not null,
                 payload               longblob     not null,
                 created_at            datetime(6)  not null,
                 trace_context         longblob     null,
                 processed_at          datetime(6)  null
             );
             """, connection);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task SeedAsync(this MySqlConnection connection, string databaseName, int seedCount)
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
            // lang=mysql
            $"INSERT INTO {databaseName}.outbox_messages (type, payload, created_at, trace_context) VALUES (@Type, @Payload, @CreatedAt, @TraceContext);",
            messages);
    }
}