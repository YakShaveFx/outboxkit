---
outline: deep
---

# Quickstart

Let's take a shortcut and see how to get started with OutboxKit, in the simplest way possible.

To start with, take a look at the existing database specific providers, and choose the one that fits your needs (assuming there's one that does). In this example, we'll use the MySQL provider.

```sh
dotnet add package YakShaveFx.OutboxKit.MySql
```

Next, you'll need to implement the `IBatchProducer` interface, that OutboxKit will invoke when messages are available to produce. Here's a simple example:

```csharp
internal sealed class FakeBatchProducer(
    ILogger<FakeBatchProducer> logger) : IBatchProducer
{
    public Task<BatchProduceResult> ProduceAsync(
        OutboxKey key,
        IReadOnlyCollection<IMessage> messages,
        CancellationToken ct)
    {
        foreach (var message in messages)
        {
            logger.LogInformation("Got message!");
        }

        return Task.FromResult(
            new BatchProduceResult 
            {
                 Ok = messages 
            });
    }
}
```

This implementation needs to be registered with the dependency injection container, so that OutboxKit can find it. It is expected to be registered as a singleton, like so:

```csharp
services.AddSingleton<IBatchProducer, FakeBatchProducer>();
```

OutboxKit works with the tables that you setup, and doesn't create anything itself. You can create an outbox table however you like and then configure OutboxKit to use it, or you can create a table that matches the default schema that OutboxKit expects. Using EF Core, using the default schema might look something like this:

```csharp
public sealed class OutboxMessage
{
    public long Id { get; init; }
    public required string Type { get; init; }
    public required byte[] Payload { get; init; }
    public DateTime CreatedAt { get; init; }
    public byte[]? TraceContext { get; init; }
}

public sealed class OutboxMessageConfiguration
    : IEntityTypeConfiguration<OutboxMessage>
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
```

When using OutboxKit MySQL defaults, the `IMessage` you'll get in your `IBatchProducer` implementation will be of type `YakShaveFx.OutboxKit.MySql.Message`.

Finally, you'll need to configure OutboxKit and the MySQL provider in the dependency injection container. As we're using defaults, we get the following simple example:

```csharp
services.AddOutboxKit(kit =>
    kit.WithMySqlPolling(p => 
        p.WithConnectionString(connectionString)));
```

And that's it! From time to time, OutboxKit will check the outbox table for new messages, and when it finds some, it will invoke your `IBatchProducer` implementation.

Now, if any of this seems somewhat useful, I encourage you to take a look at the rest of the docs to understand how things work and can be configured. [What is OutboxKit?](/intro/what-is-outboxkit) and [Concepts](/intro/concepts) are relevant starting points. Then, looking at core features, like [producing messages](/core/producing-messages) or [polling trigger optimization](/core/polling-trigger-optimization) might be useful. If you want to further drill down on the MySQL provider used in this quickstart, take a look at the [MySQL provider overview](/mysql/overview).
