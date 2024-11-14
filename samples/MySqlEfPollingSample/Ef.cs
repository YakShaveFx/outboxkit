using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YakShaveFx.OutboxKit.Core;
using YakShaveFx.OutboxKit.Core.Polling;

namespace MySqlEfPollingSample;

public sealed class SampleContext(DbContextOptions<SampleContext> options) : DbContext(options)
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
}

public sealed class OutboxMessage : IMessage
{
    public long Id { get; init; }
    public required string Type { get; init; }
    public required byte[] Payload { get; init; }
    public DateTime CreatedAt { get; init; }
    public byte[]? TraceContext { get; init; }
    public DateTime? ProcessedAt { get; init; }
}

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id);
        builder.Property(e => e.Type).HasMaxLength(128);
        builder.Property(e => e.Payload);
        builder.Property(e => e.CreatedAt);
        builder.Property(e => e.TraceContext);
        builder.Property(e => e.ProcessedAt);
    }
}

public sealed class DbSetupHostedService(IServiceProvider serviceProvider) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SampleContext>();
        await context.Database.EnsureCreatedAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class OutboxInterceptor(IOutboxTrigger trigger) : SaveChangesInterceptor
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
        CancellationToken cancellationToken = new())
    {
        if (_hasOutboxMessages)
        {
            // this isn't mandatory, but if we don't trigger it after adding messages to the outbox, they will only be published on the next polling iteration
            // if waiting for polling iterations is acceptable, then don't call this:  code gets simpler and the db is less loaded
            trigger.OnNewMessages();
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }
}