---
outline: deep
---

# Polling trigger optimization

## Overview

Typically, when talking about polling, we expect that something will happen at a regular interval, and that's about it. However, OutboxKit tries to be a bit smarter about it (but needs your help 😉).

To reduce the latency between storing a message and then making it available to be produced, while avoiding the need to poll too frequently, OutboxKit allows you to trigger a poll without waiting for the interval to pass.

This can be done with one of two available interfaces: `IOutboxTrigger` and `IKeyedOutboxTrigger`.

`IOutboxTrigger` exposes a single `OnNewMessages` method with no arguments, that should be used when you have a single outbox (i.e. no multi-provider or multi-database setup).

If you have a multi-provider or multi-database setup, you should use `IKeyedOutboxTrigger`, which exposes a single `OnNewMessages` method that takes an `OutboxKey` as the sole argument, in order for OutboxKit to know which outbox to trigger the poll for.

These interfaces are automatically registered in the DI container, so you can just inject them where you need them.

::: info Note
This optimization is only effective when the code that stores the message is running in the same process as the code that polls for new messages.

If you want to have OutboxKit running in an isolated process, you have to be okay with the latency introduced by the polling approach, or look at push alternatives.
:::

## Example usage

Let's see a couple of example of using these trigger interfaces, with the MySQL polling provider as a reference.

Let's start with the `IOutboxTrigger` interface.

Imagine we use EF Core for our application's data access layer. We could implement a `SaveChangesInterceptor`, so that every time a new message is stored in the outbox, we trigger a poll.

```csharp
public sealed class OutboxInterceptor(IOutboxTrigger trigger)
    : SaveChangesInterceptor
{
    private bool _hasOutboxMessages;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = new())
    {
        if (eventData.Context is SampleContext db 
            && db.ChangeTracker.Entries<OutboxMessage>().Any())
        {
            _hasOutboxMessages = true;
        }

        return base.SavingChangesAsync(
            eventData,
            result,
            cancellationToken);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = new())
    {
        if (_hasOutboxMessages) trigger.OnNewMessages();

        return await base.SavedChangesAsync(
            eventData,
            result,
            cancellationToken);
    }
}
```

A similar approach can be taken with the `IKeyedOutboxTrigger` interface, but you need to pass the `OutboxKey` to the `OnNewMessages` method.

```csharp
public sealed class OutboxInterceptor(
    IKeyedOutboxTrigger trigger,
    ITenantProvider tenantProvider)
    : SaveChangesInterceptor
{
    private bool _hasOutboxMessages;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = new())
    {
        if (eventData.Context is SampleContext db
            && db.ChangeTracker.Entries<OutboxMessage>().Any())
        {
            _hasOutboxMessages = true;
        }

        return base.SavingChangesAsync(
            eventData,
            result,
            cancellationToken);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (_hasOutboxMessages)
        {
            trigger.OnNewMessages(
                MySqlPollingProvider.CreateKey(
                    tenantProvider.Tenant));
        }

        return await base.SavedChangesAsync(
            eventData,
            result,
            cancellationToken);
    }
}
```

Note that these are just a couple of examples, and you can take advantage of these triggers in any way that makes sense for your application.
