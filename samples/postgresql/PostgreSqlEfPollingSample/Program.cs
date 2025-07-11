using System.Text;
using Bogus;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PostgreSqlEfPollingSample;
using YakShaveFx.OutboxKit.Core;
using YakShaveFx.OutboxKit.Core.OpenTelemetry;
using YakShaveFx.OutboxKit.PostgreSql.Polling;
using YakShaveFx.OutboxKit.PostgreSql.Shared;
using static YakShaveFx.OutboxKit.Core.OpenTelemetry.TraceContextHelpers;

const string connectionString =
    "server=localhost;port=5432;user id=user;password=pass;database=outboxkit_ef_postgres_sample";

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddDbContext<SampleContext>((s, options) =>
    {
        options.UseNpgsql(connectionString);
        options.AddInterceptors(s.GetRequiredService<OutboxInterceptor>());
    })
    .AddHostedService<DbSetupHostedService>()
    .AddScoped<OutboxInterceptor>()
    .AddSingleton<IBatchProducer, FakeBatchProducer>()
    .AddOutboxKit(kit =>
        kit
            .WithPostgreSqlPolling(p =>
                p
                    .WithConnectionString(connectionString)
                    // this is optional, only needed if not using the default table structure
                    .WithTable(t => t
                        .WithName("OutboxMessages")
                        .WithColumnSelection(["Id", "Type", "Payload", "CreatedAt", "TraceContext"])
                        .WithIdColumn("Id")
                        .WithSorting([new SortExpression("Id")])
                        .WithIdGetter(m => ((OutboxMessage)m).Id)
                        .WithMessageFactory(static r => new OutboxMessage
                        {
                            Id = r.GetInt64(0),
                            Type = r.GetString(1),
                            Payload = r.GetFieldValue<byte[]>(2),
                            CreatedAt = r.GetDateTime(3),
                            TraceContext = r.IsDBNull(4) ? null : r.GetFieldValue<byte[]>(4)
                        })
                        // this one is needed if using update processed instead of deleting immediately
                        .WithProcessedAtColumn("ProcessedAt"))
                    .WithUpdateProcessed(u => u
                        .WithCleanUpInterval(TimeSpan.FromMinutes(1))
                        .WithMaxAge(TimeSpan.FromMinutes(2)))))
    .AddSingleton(new Faker())
    .AddSingleton(TimeProvider.System);

builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("PostgreSqlEfPollingSample"))
    .WithTracing(b => b
        .AddAspNetCoreInstrumentation()
        .AddOutboxKitInstrumentation()
        .AddSource(FakeBatchProducer.ActivitySource.Name)
        .AddNpgsql()
        .AddOtlpExporter())
    .WithMetrics(b => b
        .AddAspNetCoreInstrumentation()
        // not available for .NET 8
        //.AddNpgsqlInstrumentation()
        .AddOutboxKitInstrumentation()
        .AddOtlpExporter());

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.MapPost("/produce/{count}", async (int count, Faker faker, SampleContext db) =>
{
    var messages = Enumerable.Range(0, count)
        .Select(_ => new OutboxMessage
        {
            Type = "sample",
            Payload = Encoding.UTF8.GetBytes(faker.Hacker.Verb()),
            CreatedAt = DateTime.UtcNow,
            TraceContext = ExtractCurrentTraceContext()
        });

    await db.OutboxMessages.AddRangeAsync(messages);
    await db.SaveChangesAsync();
});

app.Run();