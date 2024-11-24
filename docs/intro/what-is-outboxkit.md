---
outline: deep
---

# What is OutboxKit?

OutboxKit is a toolkit to help with implementing the transactional outbox pattern. Note, and this is important, it is not a completely ready to use, plug and play implementation of the pattern. It provides building blocks to (hopefully) greatly reduce the work of implementing things, but it's not a full blown implementation.

The focus of OutboxKit is on reading messages from an outbox and making them available to produce, in the most resilient way possible. Database setup (creating tables, indexes, etc) or even pushing messages into the outbox are outside the scope of the project.

With this in mind, in general I'd say it's best not use OutboxKit (yeah, you read it right 😅) . Using more comprehensive libraries like [Wolverine](https://wolverinefx.net), [NServiceBus](https://particular.net/nservicebus), [Brighter](https://github.com/BrighterCommand/Brighter), [MassTransit](https://masstransit.io), etc, is probably a better idea, as they give a more integrated messaging experience (plus they've been at this for much longer, and in a wider range of scenarios). This toolkit is aimed at scenarios where greater degree of customization of the whole messaging approach is wanted.
