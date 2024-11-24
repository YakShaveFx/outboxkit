---
outline: deep
---

# Polling

To implement a polling provider, there are three main things you need to do:

- implement the `IBatchFetcher` and `IBatchContext` interfaces
- implement the `IOutboxCleaner` interface (assuming you want to support the update processed messages feature)
- implement OutboxKit setup, which includes collecting any configuration you require, and calling core's `WithPolling` method

## Implementing `IBatchFetcher` and `IBatchContext`

`IBatchFetcher` and `IBatchContext` go hand in hand, and are responsible for fetching messages from the outbox and providing them to the core library.

`IBatchFetcher` is a simple interface with a single method, `FetchAndHoldAsync`, which, as the name implies, fetches messages from the outbox and holds them, to ensure that messages from the same outbox are not processed concurrently. Avoiding concurrently processing messages from the same outbox is important not only to avoid producing duplicate messages, but also to ensure that messages are processed in the correct order.

An instance of `IBatchContext` is returned by `FetchAndHoldAsync`, and includes a `Messages` property, containing the messages to produce, plus the `CompleteAsync` and `HasNextAsync` methods.

`CompleteAsync` is invoked when the messages have been processed, and gets the messages that were actually produced as an argument, which you should use to update the outbox (either by deleting the messages, or updating them to processed).

`HasNextAsync` is used to check if there are more messages to fetch from the outbox, so OutboxKit immediately tries to fetch the next batch, instead of sleeping for the polling interval.

Additionally, `IBatchContext` implements the `IAsyncDisposable` interface, which is used to give you the opportunity to release any resources that were acquired when fetching and holding the batch (e.g. connections, transactions, locks, etc).

In terms of lifetime, `IBatchFetcher` is created once per outbox, so should be registered in the DI container as a keyed singleton (more on the keyed part later). `IBatchContext` isn't fetched from the DI container, so it's up to you how you manage the lifetime. Normally, a new instance of `IBatchContext` would be created each time `FetchAndHoldAsync` is called, but if you have some good reasons, you could have a pool of `IBatchContext` instances, or even reuse the same instance. Just make sure there's no issues with reusing it across different fetches and outboxes.

## Implementing `IOutboxCleaner`

`IOutboxCleaner` is a rather simple interface, with a single method, `CleanAsync`, which returns the amount of messages that were cleaned, with no arguments (other than the typical `CancellationToken`).

Here, you should delete messages that were processed.

`IOutboxCleaner` is created once per provider, so should be registered in the DI container as a keyed singleton.

## Setup

To provide a simple way for library users to configure OutboxKit, your setup code should collect any configuration you require, plus some core required things, and call the core library's `WithPolling` method. Namely, the polling interval, the cleanup interval, as well as if cleanup should be enabled or not. Additionally, you should also collect the key the client will use for the outbox (as a `string`).

After you collect all the required configuration, calling `WithPolling` should include the aforementioned key, and something that implements `IPollingOutboxKitConfigurator`. This interface exposes a couple of methods: `ConfigureServices` and `GetCoreSettings`.

`ConfigureServices` is invoked when the core library is setting up services, and it's where you should register your `IBatchFetcher` and `IOutboxCleaner` implementations, plus any other services you require. You get the `OutboxKey` as an argument, and you should use that at least when registering `IBatchFetcher` and `IOutboxCleaner` (you could use it for any other services you require, of course).

`GetCoreSettings` is invoked when the core library is setting stuff up, and requires the configuration you collected.
