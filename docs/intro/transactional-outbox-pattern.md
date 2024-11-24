---
outline: deep
---

# Transactional Outbox Pattern

First things first, what's the problem the transactional outbox pattern addresses?

Let's imagine we have an HTTP API, exposed by *some service*. We make a `POST` request to an endpoint, which executes some logic, persists changes to a database, then produces a message to messaging infrastructure (typically this would be an event published to a topic, but could also be a command sent to a queue).

The following diagram presents this sample scenario.

[![Typical flawed flow](/transactional-outbox-pattern/typical-flawed-flow.png)](/transactional-outbox-pattern/typical-flawed-flow.png)

At first, this looks pretty reasonable. However, due to the orchestration of different dependencies across transactional boundaries, there's potential to get the system in an inconsistent state.

Imagine that after we persist the changes to the database, we're unable to produce the message for some reason, be it the broker being temporarily down, a network hiccup, or even the service itself terminating abruptly (among many other possible failure scenarios). If this happens, we might find ourselves in an inconsistent state, as the downstream services will never get the message they were supposed to.

The transactional outbox pattern presents a possible solution to tackle the described problem. The gist of it is: when we persist the business data, we also persist the data for the message we want to produce, to the same data store, within the same transaction. Because we do both operations in a transactional way, we know that either everything succeeds or nothing succeeds. With the message data in the database, we can have another component read it and produce it to the message broker. If producing fails for some reason, this component will continue retrying until it succeeds.

The following diagram showcases this example.

[![Outbox flow](/transactional-outbox-pattern/outbox-flow.png)](/transactional-outbox-pattern/outbox-flow.png)

The outbox producer can be implemented in different ways, namely using polling or push approaches. When using polling, the producer checks for new messages at a regular time interval. Alternatively, when using push, the new messages are pushed to the producer. A typical way to implement push with a relational database would be by tailing the transaction log.

The outbox producer is presented as an internal component of the service, but it's not necessarily so, it can also be implemented out of process.

As for types of databases, while the example presented assumes a relational one (as we can infer by the usage of the "table" designation), the pattern is also applicable with non-relational databases, though it might differ a bit in implementation, depending on the capabilities of the specific database.

One final important note, is that the outbox pattern helps in guaranteeing at least once delivery. This means that it's possible some messages are produced more than once, e.g. if after producing the messages, there's a failure when (or before) marking them as processed. This shouldn't be a big deal, as the message consumers should be idempotent either way, given there are a bunch of ways for the messages to reach the consumers more than once, but it's still important to keep in mind.
