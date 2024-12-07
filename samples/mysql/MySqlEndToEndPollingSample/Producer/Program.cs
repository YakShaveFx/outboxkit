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
using Microsoft.AspNetCore.Mvc;
using MySqlEndToEndPollingSample.ProducerShared;
using RabbitMQ.Client;
using YakShaveFx.OutboxKit.Core.OpenTelemetry;
using YakShaveFx.OutboxKit.Core.Polling;
using YakShaveFx.OutboxKit.MySql.Polling;
using static YakShaveFx.OutboxKit.Core.OpenTelemetry.TraceContextHelpers;

var builder = WebApplication.CreateBuilder(args);

var outOfProcessProducerEnabled = builder.Configuration.GetValue<bool?>("ENABLE_OUT_OF_PROCESS_PRODUCER") ?? false;

var connectionString = builder.Configuration.GetConnectionString("Default")!;

builder.Services
    .AddMySqlDataSource(connectionString)
    .AddSingleton(new Faker())
    .AddSingleton(TimeProvider.System)
    .AddHostedService<DbSetupHostedService>();

if (!outOfProcessProducerEnabled)
{
    var rabbitMqSettings = builder.Configuration.GetRequiredSection("RabbitMq").Get<RabbitMqSettings>()!;
    
    builder.Services
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
                        .WithPollingInterval(TimeSpan.FromSeconds(15))
                ));
}
else
{
    builder.Services.AddSingleton<IOutboxTrigger, NoOpTrigger>();
}

builder.Services
    .AddSingleton<RabbitMqProducerMetrics>()
    .AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(
        serviceName: "MySqlEndToEndPollingSample.Producer",
        serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
        serviceInstanceId: Environment.MachineName))
    .WithTracing(trace =>
    {
        trace
            .AddAspNetCoreInstrumentation()
            .AddSource("MySqlConnector")
            .AddOtlpExporter(o =>
                o.Endpoint =
                    new Uri(builder.Configuration["OpenTelemetrySettings:Endpoint"] ?? "http://localhost:4317"));

        if (!outOfProcessProducerEnabled)
        {
            trace
                .AddOutboxKitInstrumentation()
                .AddSource(RabbitMqProducerActivitySource.ActivitySourceName);
        }
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddMeter("MySqlConnector")
            .AddOtlpExporter(o =>
                o.Endpoint =
                    new Uri(builder.Configuration["OpenTelemetrySettings:Endpoint"] ?? "http://localhost:4317"));

        if (!outOfProcessProducerEnabled)
        {
            metrics
                .AddOutboxKitInstrumentation()
                .AddMeter(RabbitMqProducerMetrics.MeterName);
        }
    });


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

app.Logger.LogInformation("Out-of-process producer enabled: {Enabled}", outOfProcessProducerEnabled);

app.Run();