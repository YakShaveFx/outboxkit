using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YakShaveFx.OutboxKit.Core.Polling;
using YakShaveFx.OutboxKit.MySql;

namespace MySqlEfMultiDbPollingSample;

public sealed class SampleContext(
    DbContextOptions<SampleContext> options,
    ITenantProvider tenantProvider,
    ConnectionStringProvider connectionStringProvider) : DbContext(options)
{
    private static readonly MySqlServerVersion MySqlVersion = new(new Version(8, 0));

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseMySql(connectionStringProvider.GetConnectionString(tenantProvider.Tenant), MySqlVersion);
        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
}

public sealed class OutboxMessage
{
    public long Id { get; init; }
    public required string Type { get; init; }
    public required byte[] Payload { get; init; }
    public DateTime CreatedAt { get; init; }
    public byte[]? TraceContext { get; init; }
}

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.Type).HasColumnName("type").HasMaxLength(128);
        builder.Property(e => e.Payload).HasColumnName("payload");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.TraceContext).HasColumnName("trace_context");
    }
}

public sealed class DbSetupHostedService(IServiceProvider serviceProvider, TenantList tenantList) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var tenant in tenantList)
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<SampleContext>();
            var tenantProvider = scope.ServiceProvider.GetRequiredService<TenantProvider>();
            tenantProvider.SetTenant(tenant);
            await context.Database.EnsureCreatedAsync(cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class OutboxInterceptor(IKeyedOutboxTrigger trigger, ITenantProvider tenantProvider)
    : SaveChangesInterceptor
{
    private bool _hasOutboxMessages;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = new())
    {
        if (eventData.Context is SampleContext db && db.ChangeTracker.Entries<OutboxMessage>().Any())
        {
            _hasOutboxMessages = true;
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (_hasOutboxMessages)
        {
            // this isn't mandatory, but if we don't trigger it after adding messages to the outbox, they will only be published on the next polling iteration
            // if waiting for polling iterations is acceptable, then don't call this:  code gets simpler and the db is less loaded
            trigger.OnNewMessages(MySqlPollingProviderInfo.CreateKey(tenantProvider.Tenant));
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }
}