---
outline: deep
---

# Core overview

OutboxKit core contains the generally applicable building blocks to implement the transactional outbox pattern. It has a particular focus on the polling approach, as it is the easier to generalize, while the push approach is more dependent on database specifics.

Besides the generally applicable logic, the core exposes a set of interfaces for both the providers and end users to implement.

From a library end user perspective, most of the interaction with the core is done indirectly through the providers, though there are a couple of features that are directly exposed to the end user, regarding message production and polling trigger optimization.
