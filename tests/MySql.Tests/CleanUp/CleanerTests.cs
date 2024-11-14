using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using MySqlConnector;
using YakShaveFx.OutboxKit.MySql.CleanUp;

namespace YakShaveFx.OutboxKit.MySql.Tests.CleanUp;

[Collection(MySqlCollection.Name)]
public class CleanerTests(MySqlFixture mySqlFixture)
{
    [Theory]
    [InlineData(10, 0, 0)]
    [InlineData(10, 5, 5)]
    [InlineData(10, 10, 0)]
    [InlineData(10, 0, 5)]
    [InlineData(10, 0, 10)]
    public async Task WhenCleaningUpTheOutboxAllExpiredProcessedMessagesAreDeletedWhileRecentOrUnprocessedRemain(
        int totalCount,
        int processedExpiredCount,
        int processedRecentCount)
    {
        var schemaSettings = Defaults.Update.DefaultSchemaSettings;
        var tableConfig = Defaults.Update.TableConfigWithProcessedAt;
        var cleanupSettings = new MySqlCleanUpSettings();
        var now = new DateTimeOffset(2024, 11, 13, 18, 36, 45, TimeSpan.Zero);
        var fakeTimeProvider = new FakeTimeProvider();
        fakeTimeProvider.SetUtcNow(now);
        await using var dbCtx = await mySqlFixture.DbInit.WithDefaultSchema(schemaSettings).WithSeed(0).InitAsync();
        await using var connection = await dbCtx.DataSource.OpenConnectionAsync();

        var messages = Enumerable.Range(0, totalCount)
            .Select(i => new Message(
                Type: "test",
                Payload: JsonSerializer.SerializeToUtf8Bytes(new { i }),
                CreatedAt: (now - cleanupSettings.MaxAge).AddDays(-1).DateTime,
                ProcessedAt: i switch
                {
                    _ when i < processedExpiredCount => now.Add(-cleanupSettings.MaxAge).AddMinutes(-1).DateTime,
                    _ when i >= processedExpiredCount && i < processedExpiredCount + processedRecentCount => now.Add(-cleanupSettings.MaxAge).AddMinutes(1).DateTime,
                    _ => null
                },
                TraceContext: null))
            .ToArray();
        await SeedAsync(connection, messages);

        var sut = new Cleaner(tableConfig, cleanupSettings, dbCtx.DataSource, fakeTimeProvider);
        
        var deletedCount = await sut.CleanAsync(default);
        deletedCount.Should().Be(processedExpiredCount);
    }

    private static async Task SeedAsync(MySqlConnection connection, IEnumerable<Message> messages)
    {
        await connection.ExecuteAsync(
            // lang=mysql
            """
            INSERT INTO outbox_messages (type, payload, created_at, processed_at, trace_context)
            VALUES (@Type, @Payload, @CreatedAt, @ProcessedAt, @TraceContext);
            """,
            messages);
    }

    private record Message(string Type, byte[] Payload, DateTime CreatedAt, DateTime? ProcessedAt, byte[]? TraceContext);
}