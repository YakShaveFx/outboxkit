using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using YakShaveFx.OutboxKit.PostgreSql.CleanUp;

namespace YakShaveFx.OutboxKit.PostgreSql.Tests.CleanUp;

public class CleanerTests(PostgreSqlFixture postgresFixture)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

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
        var cleanupSettings = new PostgreSqlCleanUpSettings();
        var now = new DateTimeOffset(2024, 11, 13, 18, 36, 45, TimeSpan.Zero);
        var fakeTimeProvider = new FakeTimeProvider();
        fakeTimeProvider.SetUtcNow(now);
        await using var dbCtx = await postgresFixture.DbInit.WithDefaultSchema(schemaSettings).WithSeed(0).InitAsync();
        await using var connection = await dbCtx.DataSource.OpenConnectionAsync(_ct);

        var messages = Enumerable.Range(0, totalCount)
            .Select(i => new Message(
                Type: "test",
                Payload: JsonSerializer.SerializeToUtf8Bytes(new { i }),
                CreatedAt: (now - cleanupSettings.MaxAge).AddDays(-1).DateTime,
                ProcessedAt: i switch
                {
                    _ when i < processedExpiredCount => now.Add(-cleanupSettings.MaxAge).AddMinutes(-1).DateTime,
                    _ when i >= processedExpiredCount && i < processedExpiredCount + processedRecentCount => now
                        .Add(-cleanupSettings.MaxAge).AddMinutes(1).DateTime,
                    _ => null
                },
                TraceContext: null))
            .ToArray();
        await SeedAsync(connection, messages);

        var sut = new Cleaner(tableConfig, cleanupSettings, dbCtx.DataSource, fakeTimeProvider);

        var deletedCount = await sut.CleanAsync(_ct);
        deletedCount.Should().Be(processedExpiredCount);
    }

    private static async Task SeedAsync(NpgsqlConnection connection, IEnumerable<Message> messages)
        => await connection.ExecuteAsync(
            // lang=postgresql
            """
            INSERT INTO "OutboxMessages" ("Type", "Payload", "CreatedAt", "ProcessedAt", "TraceContext")
            VALUES (@Type, @Payload, @CreatedAt, @ProcessedAt, @TraceContext);
            """,
            messages);

    private record Message(
        string Type,
        byte[] Payload,
        DateTime CreatedAt,
        DateTime? ProcessedAt,
        byte[]? TraceContext);
}