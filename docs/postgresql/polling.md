---
outline: deep
---

# Polling

To use OutboxKit with the PostgreSQL polling provider (and others as well), you'll need to choose between two paths: accept the library defaults, making your infra match them, or make use of the library's flexibility to adapt to your existing infrastructure.

## Using the defaults

If you choose to use the defaults, you'll need to create a table that matches the schema that OutboxKit expects.

In the box you'll find the `Message` record, which looks something like this:

```csharp
public sealed record Message : IMessage
{
    public long Id { get; init; }
    public required string Type { get; init; }
    public required byte[] Payload { get; init; }
    public required DateTime CreatedAt { get; init; }
    public byte[]? TraceContext { get; init; }
}
```

The corresponding table is expected to be called `outbox_messages`, while its columns are expected to use PostgreSQL's `snake_case` naming convention, so `id`, `type`, `payload`, `created_at`, and `trace_context`.

Additionally, with these defaults, the `id` column will be used to order the messages.

Assuming these defaults, setting up the provider with DI would look something like this:

```csharp
services.AddOutboxKit(kit =>
    kit.WithPostgreSqlPolling(p => 
        p.WithConnectionString(connectionString)));
```

## Making it your own

Now, while the defaults are nice, one of the motivations for building OutboxKit in the first place, is to make it possible to adapt to specific applications and their infrastructure, which means there's a bunch of things that can be configured.

Let's start with a snippet that shows all the things you can configure:

```csharp
services.AddOutboxKit(kit =>
    kit
        .WithPostgreSqlPolling(p =>
            p
                .WithConnectionString(connectionString)
                .WithBatchSize(100)
                .WithPollingInterval(TimeSpan.FromMinutes(5))
                .WithTable(t => t
                    .WithName("OutboxMessages")
                    .WithColumnSelection(
                        [
                            "Id",
                            "Type",
                            "Payload",
                            "CreatedAt",
                            "TraceContext"
                        ])
                    .WithIdColumn("Id")
                    .WithSorting([new SortExpression("Id")])
                    .WithIdGetter(m => ((OutboxMessage)m).Id)
                    .WithMessageFactory(static r => new OutboxMessage
                    {
                        Id = r.GetInt64(0),
                        Type = r.GetString(1),
                        Payload = r.GetFieldValue<byte[]>(2),
                        CreatedAt = r.GetDateTime(3),
                        TraceContext = r.IsDBNull(4)
                            ? null 
                            : r.GetFieldValue<byte[]>(4)
                    })
                    .WithProcessedAtColumn("ProcessedAt"))
                .WithUpdateProcessed(u => u
                    .WithCleanUpInterval(TimeSpan.FromHours(1))
                    .WithMaxAge(TimeSpan.FromDays(1)))
                .WithSelectForUpdateConcurrencyControl()
                .WithAdvisoryLockConcurrencyControl()
            ));
```

So, it's not massive, but there still are a few options available.

Note that not everything is always mandatory, but there are some things that are dependent on each other, so if you set one, you'll need to set some others.

`WithConnectionString` is rather self-explanatory, and is also the only configuration that is, of course, always required.

`WithBatchSize` allows you to set the maximum number of messages that will made available to the `IBatchProducer` in one go.

 `WithPollingInterval` allows you to customize how often polling should happen.

`WithTable` is where you can configure the table you want OutboxKit to use. If you want to use the defaults, minus the table name, you can simply use `WithName` and be done with it. However, if you want to customize something else in the schema, then you need to use all the other methods (minus `WithProcessedAtColumn`, but we'll look at that later).

`WithColumnSelection` is where you specify the names of the columns that should be fetched from the table. No need to set all of them, just the ones you need for producing messages, plus the column corresponding to the id, as it will be needed to acknowledge the messages produced.

The name passed to `WithIdColumn` will be used when acknowledging the messages produced.

`WithSorting` receives a collection of column names, as well as a sort direction, which are used to sort the rows when fetching them from the outbox.

When acknowledging the messages, the function passed to `WithIdGetter` will be used to get the id from the message instance, which will then be used for message completion.

Because of all the schema customization, the library has no idea how to construct a message instance. For this reason, you need to provide your own implementation using `WithMessageFactory`. You get a `PostgreSqlDataReader` as an argument, and you need to return an instance of something that implements `IMessage`. The order in which you configure the columns in `WithColumnSelection` is important, as it matches the indexes in the `PostgreSqlDataReader`.

Let's talk about `WithUpdateProcessed`, then come back to `WithProcessedAtColumn`.

By default, OutboxKit will immediately delete the messages that have been produced. However, if you want to keep them around for a while, you can change the strategy to mark them as processed instead. To do this, you use `WithUpdateProcessed`.

When using `WithUpdateProcessed`, you can configure how often the messages should be cleaned up using `WithCleanUpInterval`, and how old the messages should be before they are cleaned up using `WithMaxAge`.

Note that, if you use `WithUpdateProcessed`, you must use `WithProcessedAtColumn`, in order for the library to do its magic. When marking the messages as processed, OutboxKit will set the column to a `DateTime` in UTC, obtained from a [`TimeProvider`](https://learn.microsoft.com/en-us/dotnet/api/system.timeprovider) it gets from DI.

`WithSelectForUpdateConcurrencyControl` and `WithAdvisoryLockConcurrencyControl` are two available options to handle concurrency control. In some scenarios, using advisory locks might provide performance benefits when compared to "SELECT ... FOR UPDATE", given it avoids locking the actual rows in the outbox table as they're being produced.

## Multi-database

If your application uses multiple PostgreSQL databases, and you need an outbox for each of them (for example you have a multi-tenant application, where each tenant uses a different database), everything we discussed so far still applies, you just need to tweak things very slightly.

`WithPostgreSqlPolling` has an overload that takes a `string` as the first argument, allowing you to identify the outbox.

Taking the defaults approach as an example, you could set up two outboxes like this:

```csharp
services.AddOutboxKit(kit =>
    kit
        .WithPostgreSqlPolling(
            tenantOne,
            p => p.WithConnectionString(connectionStringOne))
        .WithPostgreSqlPolling(
            tenantTwo,
            p => p.WithConnectionString(connectionStringTwo)));
```

As you can infer, this means you can not only have multiple databases, but you can also configure them differently (not sure it's the most relevant thing ever, but hey, it works).

As discussed in [Core/Producing messages](/core/producing-messages), the `IBatchProducer` `ProduceAsync` method receives an `OutboxKey`, composed by a provider key (`"mysql_polling"` in this case) and a client key, which is what you passed to `WithPostgreSqlPolling`. If you only have one outbox and don't set the key, you'll get the `string` `"default"`.
