using System.Text.Json;
using Bogus;
using MySqlConnector;
using MySqlEndToEndPollingSample.Producer;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using YakShaveFx.OutboxKit.Core;
using YakShaveFx.OutboxKit.MySql;
using Dapper;
using RabbitMQ.Client;
using YakShaveFx.OutboxKit.Core.OpenTelemetry;
using YakShaveFx.OutboxKit.Core.Polling;
using YakShaveFx.OutboxKit.MySql.Polling;
using static YakShaveFx.OutboxKit.Core.OpenTelemetry.TraceContextHelpers;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")!;
var rabbitMqSettings = builder.Configuration.GetRequiredSection("RabbitMq").Get<RabbitMqSettings>()!;
builder.Services
    .AddMySqlDataSource(connectionString)
    .AddSingleton(rabbitMqSettings)
    .AddSingleton<IConnection>(_ =>
    {
        var factory = new ConnectionFactory { HostName = rabbitMqSettings.Host, Port = rabbitMqSettings.Port };
        return factory.CreateConnection();
    })
    .AddSingleton<IBatchProducer, RabbitMqProducer>()
    .AddOutboxKit(kit =>
        kit
            .WithMySqlPolling(p => 
                p
                    .WithConnectionString(connectionString)
                    .WithBatchSize(builder.Configuration.GetValue<int?>("OutboxKit:Polling:BatchSize") ?? 100)
                    .WithAdvisoryLockConcurrencyControl()
                ))
    .AddSingleton(new Faker())
    .AddSingleton(TimeProvider.System)
    .AddHostedService<DbSetupHostedService>();

builder.Services
    .AddSingleton<RabbitMqProducerMetrics>()
    .AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(
        serviceName: "MySqlEndToEndPollingSample.Producer",
        serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
        serviceInstanceId: Environment.MachineName))
    .WithTracing(b => b
        .AddAspNetCoreInstrumentation()
        .AddOutboxKitInstrumentation()
        .AddSource("MySqlConnector")
        .AddSource(RabbitMqProducerActivitySource.ActivitySourceName)
        .AddOtlpExporter(o =>
            o.Endpoint = new Uri(builder.Configuration["OpenTelemetrySettings:Endpoint"] ?? "http://localhost:4317")))
    .WithMetrics(b => b
        .AddAspNetCoreInstrumentation()
        .AddMeter("MySqlConnector")
        .AddOutboxKitInstrumentation()
        .AddMeter(RabbitMqProducerMetrics.MeterName)
        .AddOtlpExporter(o =>
            o.Endpoint = new Uri(builder.Configuration["OpenTelemetrySettings:Endpoint"] ?? "http://localhost:4317")));

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.MapPost("/produce-something", async (
    Faker faker,
    TimeProvider timeProvider,
    MySqlDataSource dataSource,
    IOutboxTrigger trigger) =>
{
    var @event = new SampleEvent
    {
        Id = Guid.NewGuid(),
        Verb = faker.Hacker.Verb()
    };

    var outboxMessage = new Message
    {
        Type = nameof(SampleEvent),
        Payload = JsonSerializer.SerializeToUtf8Bytes(@event),
        CreatedAt = timeProvider.GetUtcNow().DateTime,
        TraceContext = ExtractCurrentTraceContext()
    };

    await using var connection = await dataSource.OpenConnectionAsync();
    await connection.ExecuteAsync(
        // lang=mysql
        "INSERT INTO outbox_messages (type, payload, created_at, trace_context) VALUES (@Type, @Payload, @CreatedAt, @TraceContext)",
        outboxMessage);
    trigger.OnNewMessages();
});

app.Run();