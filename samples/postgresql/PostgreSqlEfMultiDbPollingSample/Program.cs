using System.Collections.Frozen;
using System.Text;
using Bogus;
using PostgreSqlEfMultiDbPollingSample;
using YakShaveFx.OutboxKit.Core;
using YakShaveFx.OutboxKit.PostgreSql.Polling;

const string tenantOne = "tenant-one";
const string tenantTwo = "tenant-two";
const string connectionStringOne =
    "server=localhost;port=5432;user id=user;password=pass;database=outboxkit_ef_postgres_sample_tenant_one";
const string connectionStringTwo =
    "server=localhost;port=5432;user id=user;password=pass;database=outboxkit_ef_postgres_sample_tenant_two";

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
    .AddSingleton<IBatchProducer, FakeBatchProducer>()
    .AddOutboxKit(kit =>
        kit
            .WithPostgreSqlPolling(
                tenantOne,
                p =>
                    p
                        .WithConnectionString(connectionStringOne)
                        // optional, only needed if the default isn't the desired value
                        .WithPollingInterval(TimeSpan.FromSeconds(30))
                        // optional, only needed if the default isn't the desired value
                        .WithBatchSize(5))
            .WithPostgreSqlPolling(tenantTwo, p => p.WithConnectionString(connectionStringTwo)))
    .AddSingleton(new Faker())
    .AddSingleton(TimeProvider.System);

var app = builder.Build();

app.UseMiddleware<TenantMiddleware>();

app.MapGet("/", () => "Hello World!");

app.MapPost("/produce/{count}", async (int count, Faker faker, SampleContext db, ITenantProvider tp) =>
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