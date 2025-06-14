using System.Text;
using Bogus;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDbMultiDbPollingSample;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using YakShaveFx.OutboxKit.Core;
using YakShaveFx.OutboxKit.Core.OpenTelemetry;
using YakShaveFx.OutboxKit.Core.Polling;
using YakShaveFx.OutboxKit.MongoDb;
using YakShaveFx.OutboxKit.MongoDb.Polling;
using static YakShaveFx.OutboxKit.Core.OpenTelemetry.TraceContextHelpers;

const string tenantOne = "tenant-one";
const string tenantTwo = "tenant-two";
const string connectionString = "mongodb://localhost:27017";
const string tenantOneDatabaseName = "outboxkit_mongo_polling_sample_tenant_one";
const string tenantTwoDatabaseName = "outboxkit_mongo_polling_sample_tenant_two";
const string collectionName = "outbox_messages";

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddKeyedSingleton(tenantOne, new MongoClient(connectionString).GetDatabase(tenantOneDatabaseName))
    .AddKeyedSingleton(tenantTwo, new MongoClient(connectionString).GetDatabase(tenantTwoDatabaseName))
    .AddSingleton<IBatchProducer, FakeBatchProducer>()
    .AddScoped<TenantProvider>()
    .AddScoped<ITenantProvider>(s => s.GetRequiredService<TenantProvider>())
    .AddSingleton(new TenantList(new HashSet<string>([tenantOne, tenantTwo])))
    .AddOutboxKit(kit =>
        kit
            .WithMongoDbPolling(
                tenantOne,
                p => p.WithDatabaseFactory((k, s) => s.GetRequiredKeyedService<IMongoDatabase>(tenantOne)))
            .WithMongoDbPolling(
                tenantTwo,
                p => p.WithDatabaseFactory((k, s) => s.GetRequiredKeyedService<IMongoDatabase>(tenantTwo))))
    .AddSingleton(new Faker())
    .AddSingleton(TimeProvider.System);

builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("MongoDbMultiDbPollingSample"))
    .WithTracing(b => b
        .AddAspNetCoreInstrumentation()
        .AddOutboxKitInstrumentation()
        .AddSource(FakeBatchProducer.ActivitySource.Name)
        .AddOtlpExporter())
    .WithMetrics(b => b
        .AddAspNetCoreInstrumentation()
        .AddOutboxKitInstrumentation()
        .AddOtlpExporter());

var app = builder.Build();

app.UseMiddleware<TenantMiddleware>();

app.MapGet("/", () => "Hello World!");

app.MapPost(
    "/produce/{count}",
    async (int count, Faker faker, IServiceProvider services, IKeyedOutboxTrigger trigger, ITenantProvider tp) =>
    {
        var messages = Enumerable.Range(0, count)
            .Select(_ => new Message
            {
                Type = "sample",
                Payload = Encoding.UTF8.GetBytes(faker.Hacker.Verb()),
                CreatedAt = DateTime.UtcNow,
                TraceContext = ExtractCurrentTraceContext()
            });

        var db = services.GetRequiredKeyedService<IMongoDatabase>(tp.Tenant);
        await db.GetCollection<Message>(collectionName).InsertManyAsync(messages);
        trigger.OnNewMessages(MongoDbPollingProvider.CreateKey(tp.Tenant));
    });

app.Run();