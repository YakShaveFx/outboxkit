using System.Text;
using Bogus;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDbPollingSample;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using YakShaveFx.OutboxKit.Core;
using YakShaveFx.OutboxKit.Core.OpenTelemetry;
using YakShaveFx.OutboxKit.Core.Polling;
using YakShaveFx.OutboxKit.MongoDb.Polling;
using static YakShaveFx.OutboxKit.Core.OpenTelemetry.TraceContextHelpers;

const string connectionString = "mongodb://localhost:27017";
const string databaseName = "outboxkit_mongo_polling_sample";
const string collectionName = "OutboxMessages";

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddSingleton(new MongoClient(connectionString).GetDatabase(databaseName))
    .AddSingleton<IBatchProducer, FakeBatchProducer>()
    .AddOutboxKit(kit =>
        kit
            .WithMongoDbPolling(p =>
                p
                    .WithDatabaseFactory((k, s) => s.GetRequiredService<IMongoDatabase>())
                    .WithCollection<OutboxMessage, ObjectId>(c => c
                        .WithName(collectionName)
                        .WithIdSelector(m => m.Id)
                        .WithSort(new SortDefinitionBuilder<OutboxMessage>().Ascending(m => m.Id))
                        // only needed if we want to update processed instead of deleting immediately
                        .WithProcessedAtSelector(m => m.ProcessedAt))
                    .WithUpdateProcessed(u => u
                        .WithCleanUpInterval(TimeSpan.FromMinutes(1))
                        .WithMaxAge(TimeSpan.FromMinutes(2)))
                    .WithDistributedLock(l => l
                        .WithChangeStreamsEnabled(true))))
    .AddSingleton(new Faker())
    .AddSingleton(TimeProvider.System);

builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("MongoDbPollingSample"))
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

app.MapGet("/", () => "Hello World!");

app.MapPost(
    "/produce/{count}",
    async (int count, Faker faker, IMongoDatabase db, IOutboxTrigger trigger) =>
{
    var messages = Enumerable.Range(0, count)
        .Select(_ => new OutboxMessage
        {
            Type = "sample",
            Payload = Encoding.UTF8.GetBytes(faker.Hacker.Verb()),
            CreatedAt = DateTime.UtcNow,
            TraceContext = ExtractCurrentTraceContext()
        });

    await db.GetCollection<OutboxMessage>(collectionName).InsertManyAsync(messages);
    trigger.OnNewMessages();
});

app.Run();