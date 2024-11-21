using System.Text;
using Bogus;
using Microsoft.EntityFrameworkCore;
using MySqlEfPollingSample;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using YakShaveFx.OutboxKit.Core;
using YakShaveFx.OutboxKit.Core.OpenTelemetry;
using YakShaveFx.OutboxKit.MySql.Polling;
using static YakShaveFx.OutboxKit.Core.OpenTelemetry.TraceContextHelpers;

const string connectionString =
    "server=localhost;port=3306;database=outboxkit_ef_mysql_sample;user=root;password=root;";

var builder = WebApplication.CreateBuilder(args);
var mySqlVersion = new MySqlServerVersion(new Version(8, 0));

builder.Services
    .AddDbContext<SampleContext>((s, options) =>
    {
        options.UseMySql(connectionString, mySqlVersion);
        options.AddInterceptors(s.GetRequiredService<OutboxInterceptor>());
    })
    .AddHostedService<DbSetupHostedService>()
    .AddScoped<OutboxInterceptor>()
    .AddSingleton<IBatchProducer, FakeBatchProducer>()
    .AddOutboxKit(kit =>
        kit
            .WithMySqlPolling(p =>
                p
                    .WithConnectionString(connectionString)
                    // this is optional, only needed if not using the default table structure
                    .WithTable(t => t
                        .WithName("OutboxMessages")
                        .WithColumnSelection(["Id", "Type", "Payload", "CreatedAt", "TraceContext"])
                        .WithIdColumn("Id")
                        .WithOrderByColumn("Id")
                        .WithIdGetter(m => ((OutboxMessage)m).Id)
                        .WithMessageFactory(r => new OutboxMessage
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
    .ConfigureResource(r => r.AddService("MySqlEfPollingSample"))
    .WithTracing(b => b
        .AddAspNetCoreInstrumentation()
        .AddOutboxKitInstrumentation()
        .AddSource(FakeBatchProducer.ActivitySource.Name)
        .AddSource("MySqlConnector")
        .AddOtlpExporter())
    .WithMetrics(b => b
        .AddAspNetCoreInstrumentation()
        .AddMeter("MySqlConnector")
        .AddOutboxKitInstrumentation()
        .AddOtlpExporter());

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.MapPost("/publish/{count}", async (int count, Faker faker, SampleContext db) =>
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