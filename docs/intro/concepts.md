---
outline: deep
---

# Concepts

This page describes the main concepts of OutboxKit.

## Core vs providers

OutboxKit is divided into two main parts: the core and the providers.

The core contains the generally applicable building blocks to implement the transactional outbox pattern. It has a particular focus on the polling approach, as it is the easier to generalize, while the push approach is more dependent on database specifics.

Besides the generally applicable logic, the core exposes a set of interfaces for both the providers and end users to implement.

The providers implement the logic to interact with specific databases. For more information on building a provider, checkout the dedicated pages, one focusing on [polling](/building-a-provider/polling), while the other focuses on [push](/building-a-provider/push).

To start using OutboxKit in your project, you'll need to choose a provider that fits your needs (or implement your own, if a fit doesn't exist).

## Polling & push

OutboxKit aims to support two main approaches to implementing the outbox pattern: polling and push (though not all providers support both).

Polling, as you might expect, involves periodically checking the database for new messages to produce. It is less efficient than push, but is easier to implement, and depending on the particular needs of an application, it is often good enough.

Push, on the other hand, involves the database notifying the application when a new message is available. A common way to implement this with relational databases is to use transaction log tailing.

To make the polling approach more efficient, OutboxKit provides a way to minimize the need for polling and, at the same time, reduce latency. For more information, check out the page on the [polling trigger optimization](/core/polling-trigger-optimization).

## Message production

Regardless of using polling or push, OutboxKit needs a way to produce the messages. As the integration with specific messaging systems is outside the scope of the project, OutboxKit exposes a set of types to allow for the user to implement message production, including the `IBatchProducer` interface.

More information on how to implement message production can be found in the [dedicated page](/core/producing-messages).

## Clean up

OutboxKit can be configured to acknowledge produced messages in one of two ways: by immediately deleting them from the outbox table, or by marking them as processed.

While immediate deletion is the most efficient option, some might prefer to keep the messages around for a while after being produced. Immediate deletion is the default behavior, and by design means no additional clean up is required. When OutboxKit is configured to mark messages as processed, a background service executes to clean up the table from time to time.

## Multi-provider & multi-database

OutboxKit is designed to support multiple providers and multiple databases at the same time.

Multi-provider means you can have more than one provider running at the same time in the same application, allowing you to have an outbox, for example, in MySQL and another in MongoDB, if you have your business data spread like that and it's useful to have the outbox living with each of them.

As for multi-database, similarly to multi-provider, it allows you to have multiple databases for the same provider at the same time in one application. This would be useful, for example, in multi-tenant scenarios, where business data for each tenant is stored in a different database, allowing for the outbox to live with its respective tenant.
