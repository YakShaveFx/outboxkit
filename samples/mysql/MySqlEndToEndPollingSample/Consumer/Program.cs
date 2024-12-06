using MySqlEndToEndPollingSample.Consumer;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

var rabbitMqSettings = builder.Configuration.GetRequiredSection("RabbitMq").Get<RabbitMqSettings>()!;
builder.Services
    .AddSingleton(rabbitMqSettings)
    .AddSingleton<IConnection>(_ =>
    {
        var factory = new ConnectionFactory { HostName = rabbitMqSettings.Host, Port = rabbitMqSettings.Port };
        return factory.CreateConnection();
    })
    .AddHostedService<RabbitMqConsumer>();

builder.Services
    .AddSingleton<RabbitMqConsumerMetrics>()
    .AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(
        serviceName: "MySqlEndToEndPollingSample.Consumer",
        serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
        serviceInstanceId: Environment.MachineName))
    .WithTracing(b => b
        .AddAspNetCoreInstrumentation()
        .AddSource(RabbitMqProducerActivitySource.ActivitySourceName)
        .AddOtlpExporter(o =>
            o.Endpoint = new Uri(builder.Configuration["OpenTelemetrySettings:Endpoint"] ?? "http://localhost:4317")))
    .WithMetrics(b => b
        .AddAspNetCoreInstrumentation()
        .AddMeter(RabbitMqConsumerMetrics.MeterName)
        .AddOtlpExporter(o =>
            o.Endpoint = new Uri(builder.Configuration["OpenTelemetrySettings:Endpoint"] ?? "http://localhost:4317")));

var app = builder.Build();

app.MapGet("/", () => "Hello World!");


app.Run();