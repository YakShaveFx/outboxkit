using MySqlEndToEndPollingSample.ProducerShared;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RabbitMQ.Client;
using YakShaveFx.OutboxKit.Core;
using YakShaveFx.OutboxKit.Core.OpenTelemetry;
using YakShaveFx.OutboxKit.MySql.Polling;

var builder = WebApplication.CreateBuilder(args);

var outOfProcessProducerEnabled = builder.Configuration.GetValue<bool?>("ENABLE_OUT_OF_PROCESS_PRODUCER") ?? false;

var connectionString = builder.Configuration.GetConnectionString("Default")!;
var rabbitMqSettings = builder.Configuration.GetRequiredSection("RabbitMq").Get<RabbitMqSettings>()!;

if (outOfProcessProducerEnabled)
{
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
    
    builder.Services
        .AddSingleton<RabbitMqProducerMetrics>()
        .AddOpenTelemetry()
        .ConfigureResource(r => r.AddService(
            serviceName: "MySqlEndToEndPollingSample.OutOfProcessProducer",
            serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
            serviceInstanceId: Environment.MachineName))
        .WithTracing(trace =>
        {
            trace
                .AddAspNetCoreInstrumentation()
                .AddOutboxKitInstrumentation()
                .AddSource("MySqlConnector")
                .AddSource(RabbitMqProducerActivitySource.ActivitySourceName)
                .AddOtlpExporter(o =>
                    o.Endpoint =
                        new Uri(builder.Configuration["OpenTelemetrySettings:Endpoint"] ?? "http://localhost:4317"));
        })
        .WithMetrics(metrics =>
        {
            metrics
                .AddAspNetCoreInstrumentation()
                .AddMeter("MySqlConnector")
                .AddOutboxKitInstrumentation()
                .AddMeter(RabbitMqProducerMetrics.MeterName)
                .AddOtlpExporter(o =>
                    o.Endpoint =
                        new Uri(builder.Configuration["OpenTelemetrySettings:Endpoint"] ?? "http://localhost:4317"));
        });
}




var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.Logger.LogInformation("Out-of-process producer enabled: {Enabled}", outOfProcessProducerEnabled);

app.Run();
