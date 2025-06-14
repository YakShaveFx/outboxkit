---
outline: deep
---

# Polling

To use OutboxKit with the MongoDB polling provider (and others as well), you'll need to choose between two paths: accept the library defaults, making your infra match them, or make use of the library's flexibility to adapt to your existing infrastructure.

::: tip
Don't skip the [important notes](#important-notes) section at the end of the page.
:::

## Using the defaults

In the box you'll find the `Message` record, which looks something like this:

```csharp
public sealed record Message : IMessage
{
    public ObjectId Id { get; init; }
    public required string Type { get; init; }
    public required byte[] Payload { get; init; }
    public required DateTime CreatedAt { get; init; }
    public byte[]? TraceContext { get; init; }
}
```

By default, these messages will be stored in a collection named `outbox_messages`. Additionally, with these defaults, the `Id` property will be used to order the messages.

Because MongoDB, unlike relational databases, doesn't support traditional locking mechanisms (e.g. `SELECT ... FOR UPDATE`), OutboxKit implements its own locking mechanism. This locking mechanism requires the use of an auxiliary collection, which by default is named `outbox_locks`.

Assuming you're happy with the out of the box defaults, setting up the provider with DI would look something like this:

```csharp
services.AddOutboxKit(kit =>
    kit.WithMongoDbPolling(p => 
        p.WithDatabaseFactory((_, s) => s.GetRequiredService<IMongoDatabase>()));
```

Note that this assumes a singleton `IMongoDatabase` is registered in the DI container, but you can do whatever you want in `WithDatabaseFactory`, as long as it returns an `IMongoDatabase` instance.

## Making it your own

Now, while the defaults are nice, one of the motivations for building OutboxKit in the first place, is to make it possible to adapt to specific applications and their infrastructure, which means there's a bunch of things that can be configured.

Let's start with a snippet that shows all the things you can configure:

```csharp
services.AddOutboxKit(kit =>
    kit
        .WithMongoDbPolling(p =>
            p
                .WithDatabaseFactory((_, s) => s.GetRequiredService<IMongoDatabase>())
                .WithBatchSize(100)
                .WithPollingInterval(TimeSpan.FromMinutes(5))
                .WithCollection<OutboxMessage, ObjectId>(c => c
                    .WithName("OutboxMessages")
                    .WithIdSelector(m => m.Id)
                    .WithSort(new SortDefinitionBuilder<OutboxMessage>().Ascending(m => m.Id))
                    .WithProcessedAtSelector(m => m.ProcessedAt))
                .WithUpdateProcessed(u => u
                    .WithCleanUpInterval(TimeSpan.FromHours(1))
                    .WithMaxAge(TimeSpan.FromDays(1)))
                .WithDistributedLock(l => l
                    .WithCollectionName("OutboxLocks")
                    .WithId("OutboxLock")
                    .WithOwner(Environment.MachineName)
                    .WithChangeStreamsEnabled(true))));
```

So, it's not massive, but there still are a few options available.

Note that not everything is always mandatory, but there are some things that are dependent on each other, so if you set one, you'll need to set some others.

`WithDatabaseFactory` is the way to get a `IMongoDatabase` instance for OutboxKit to interact with the database. It is the only configuration that is always required.

`WithBatchSize` allows you to set the maximum number of messages that will be made available to the `IBatchProducer` in one go.

`WithPollingInterval` allows you to customize how often polling should happen.

`WithCollection` is where you can configure the collection you want OutboxKit to use. If you're fine with OutboxKit's defaults, no need to call `WithCollection`, but if you want to customize something, then you need to configure everything, using all the exposed builder methods (minus `WithProcessedAtSelector`, but we'll look at that later).

`WithName` allows configuring the outbox message collection name.

`WithIdSelector` allows you to configure the id selector for the outbox message, which will be used when acknowledging the messages produced.

`WithSort` is the way to configure how to sort the messages as they're fetched from the outbox collection and made available to produce.

Let's talk about `WithUpdateProcessed`, then come back to `WithProcessedAtSelector`.

By default, OutboxKit will immediately delete the messages that have been produced. However, if you want to keep them around for a while, you can change the strategy to mark them as processed instead. To do this, you use `WithUpdateProcessed`.

When using `WithUpdateProcessed`, you can configure how often the messages should be cleaned up using `WithCleanUpInterval`, and how old the messages should be before they are cleaned up using `WithMaxAge`.

Note that, if you use `WithUpdateProcessed`, you must use `WithProcessedAtSelector`, in order for the library to do its magic. When marking the messages as processed, OutboxKit will set the column to a `DateTime` in UTC, obtained from a [`TimeProvider`](https://learn.microsoft.com/en-us/dotnet/api/system.timeprovider) it gets from DI.

As mentioned earlier, because MongoDB doesn't support traditional locking mechanisms, OutboxKit implements a distributed locking mechanism itself, making use of an auxiliary collection for it. You can tweak some aspects of this, by using `WithDistributedLock`. You can tweak the collection name with `WithCollectionName`, the id associated with the lock with `WithId` (should be the same in all instances of the application interacting with the outbox), the owner of the lock with `WithOwner` (should be different per instance of the application interacting with the outbox), and whether or not to use [MongoDB change streams](https://www.mongodb.com/docs/manual/changeStreams/) with `WithChangeStreamsEnabled`. The default values for these are `outbox_locks`, `outbox_lock`, [`Environment.MachineName`](https://learn.microsoft.com/en-us/dotnet/api/system.environment.machinename) and `false`, respectively.

## Multi-database

If your application uses multiple MongoDB databases, and you need an outbox for each of them (for example, you have a multi-tenant application, where each tenant uses a different database), everything we discussed so far still applies, you just need to tweak things very slightly.

`WithMongoDbPolling` has an overload that takes a `string` as the first argument, allowing you to identify the outbox.

Taking the defaults approach as an example, you could set up two outboxes like this:

```csharp
services.AddOutboxKit(kit =>
    kit
        .WithMongoDbPolling(
            tenantOne,
            p => p.WithDatabaseFactory((k, s) => s.GetRequiredKeyedService<IMongoDatabase>(tenantOne)))
        .WithMongoDbPolling(
            tenantTwo,
            p => p.WithDatabaseFactory((k, s) => s.GetRequiredKeyedService<IMongoDatabase>(tenantTwo))));
```

As you can infer, this means you can not only have multiple databases, but you can also configure them differently (not sure it's the most relevant thing ever, but hey, it works).

You'll notice we're using `GetRequiredKeyedService` instead of `GetRequiredService`. You don't have to do it like this, but it's a simple way to have different dependencies resolved for different contexts, like a multi-tenant application.

As discussed in [Core/Producing messages](/core/producing-messages), the `IBatchProducer` `ProduceAsync` method receives an `OutboxKey`, composed by a provider key (`"mongodb_polling"` in this case) and a client key, which is what you passed to `WithMongoDbPolling`. If you only have one outbox and don't set the key, you'll get the `string` `"default"`.

The `k` parameter shown in the example above in the `WithDatabaseFactory` method is also the aforementioned `OutboxKey`, and it's passed in case it's useful to resolve the `IMongoDatabase` instance. If you don't need it, you can just ignore it.

## Important notes

- Due to the need to implement a distributed locking mechanism, not just using something provided by the database itself, makes the likelihood of bugs in this provider higher than in others. Hopefully it's implemented well, but if you find any issues, please report them.
- For simplicity and testability, the distributed locking mechanism uses the date/time of the machine running the application to determine lock expiration. For this reason, when running multiple instances of the application, it's important that the clocks are in sync, otherwise there might be unexpected behavior. Other approaches might be considered, but at this point, it seemed an acceptable approach.
- This implementation of the MongoDB polling provider was designed exclusively to be used with a primary database instance. It wasn't thought or tested to be used with sharded clusters.