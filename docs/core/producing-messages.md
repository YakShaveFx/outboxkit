---
outline: deep
---

# Producing messages

## Overview

OutboxKit is mostly focused on reading messages from the outbox and making them available to be produced. That means the responsibility of producing the messages is left to the library's end user.

To register to get messages to produce, you need to implement the `IBatchProducer` interface. This interface has a single method, `ProduceAsync`, which has three arguments: an `OutboxKey`, a collection of messages to produce and a `CancellationToken`, then returns a `BatchProduceResult`.

The collection of messages and the `CancellationToken` are rather obvious in their purpose, but the `OutboxKey` might need some explanation. The `OutboxKey` is a unique identifier for the outbox that the messages belong to. It is composed by two keys, one for the provider (e.g. `mysql_polling`) and one for the outbox itself, which you can configure when setting things up. This is only potentially interesting if you are taking advantage of the multi-provider and/or multi-database capabilities of OutboxKit.

The `BatchProduceResult` type, includes a property `Ok`, which should be set with collection of messages that were successfully produced. OutboxKit will use this to only acknowledge the messages you provided. Note that depending on the provider implementation, it might not be possible to complete the messages in cases of partial success, particularly if not matching the sequence in which they were provided to produce. Look closely at the provider docs for information on potential issues.

## Setup

OutboxKit will fetch the `IBatchProducer` implementation from the DI container, and keep it during the lifetime of the application. This means you should register it as singleton. If you need some scoped dependencies for you implementation, you should create and dispose of a scope internally, using the `IServiceScopeFactory` that you can get from the DI container.

Typical registration of the `IBatchProducer` might look like this:

```csharp
services.AddSingleton<IBatchProducer, MyBatchProducer>();
```

## Ordering

OutboxKit maintains the order of the messages, in case that's something you require. When configuring the provider, you can specify how to order the messages (e.g. when using the MySQL polling provider, you can specify a column to use in the order by clause), or if you use the default schema, some default ordering will be used.

After getting the messages passed in to your implementation of `IBatchProducer`, it's up to you to do things in a way to ensure the ordering you require. For example, if for some reason you fail to produce a message, you shouldn't produce any message that's required to be produced after it. In such a situation, you should just return the `BatchProduceResult` with the messages that were successfully produced, the rest will be retried in the next batch.

## Performance considerations

Keep in mind that while you're producing messages, OutboxKit is blocked, in more than one way (depending on the provider).

The most obvious way in which OutboxKit is blocked, is that no new messages are going to be read and made available to produce in the meantime, so if you have a lot of messages coming in, you need to get things done asap.

Less obvious, and more provider-specific, is that the provider needs to do something to keep the messages from being read again (particularly relevant in polling implementations). For example, in a relational database implementation, the provider might use a transaction to implement this, and as we know, it's good practice to keep transactions as short as possible.
