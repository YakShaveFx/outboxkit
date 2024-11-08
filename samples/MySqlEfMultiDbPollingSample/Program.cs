using System.Collections.Frozen;
using System.Text;
using Bogus;
using MySqlEfMultiDbPollingSample;
using YakShaveFx.OutboxKit.Core;
using YakShaveFx.OutboxKit.MySql.Polling;

const string tenantOne = "tenant-one";
const string tenantTwo = "tenant-two";
const string connectionStringOne =
    "server=localhost;port=3306;database=outboxkit_ef_mysql_sample_tenant_one;user=root;password=root;";
const string connectionStringTwo =
    "server=localhost;port=3306;database=outboxkit_ef_mysql_sample_tenant_two;user=root;password=root;";

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddDbContext<SampleContext>((s, options) =>
    {
        options.AddInterceptors(s.GetRequiredService<OutboxInterceptor>());
    })
    .AddHostedService<DbSetupHostedService>()
    .AddScoped<OutboxInterceptor>()
    .AddScoped<TenantProvider>()
    .AddScoped<ITenantProvider>(s => s.GetRequiredService<TenantProvider>())
    .AddSingleton(new TenantList(new HashSet<string>([tenantOne, tenantTwo])))
    .AddSingleton(new ConnectionStringProvider(new Dictionary<string, string>
    {
        [tenantOne] = connectionStringOne,
        [tenantTwo] = connectionStringTwo
    }.ToFrozenDictionary()))
    .AddSingleton<FakeBatchProducer>()
    .AddOutboxKit(kit =>
        kit
            .WithBatchProducer<FakeBatchProducer>()
            .WithMySqlPolling(
                tenantOne,
                p =>
                    p
                        .WithConnectionString(connectionStringOne)
                        // optional, only needed if the default isn't the desired value
                        .WithPollingInterval(TimeSpan.FromSeconds(30))
                        // optional, only needed if the default isn't the desired value
                        .WithBatchSize(5))
            .WithMySqlPolling(tenantTwo, p => p.WithConnectionString(connectionStringTwo)))
    .AddSingleton(new Faker())
    .AddSingleton(TimeProvider.System);

var app = builder.Build();

app.UseMiddleware<TenantMiddleware>();

app.MapGet("/", () => "Hello World!");

app.MapPost("/publish/{count}", async (int count, Faker faker, SampleContext db, ITenantProvider tp) =>
{
    var messages = Enumerable.Range(0, count)
        .Select(_ => new OutboxMessage
        {
            Type = "sample",
            Payload = Encoding.UTF8.GetBytes($"{faker.Hacker.Verb()} from {tp.Tenant}"),
            CreatedAt = DateTime.UtcNow,
            TraceContext = null
        });

    await db.OutboxMessages.AddRangeAsync(messages);
    await db.SaveChangesAsync();
});

app.Run();