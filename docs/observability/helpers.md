---
outline: deep
---

# Helpers

Because making your messaging code observable might be a bit annoying, OutboxKit includes a couple of helpers to make it easier to integrate your messaging infrastructure with distributed tracing.

These helpers are available through the `YakShaveFx.OutboxKit.Core.OpenTelemetry` package.

## `TraceContextHelpers`

The static `TraceContextHelpers` class provides a couple of methods to help you extract and restore trace context information.

If you looked at the [quickstart](/intro/quickstart), you might have noticed that the outbox message includes a `TraceContext` property. The goal of this is to be able to restore the original trace context when producing the message.

`ExtractCurrentTraceContext` extracts the ambient trace context information and returns it as `byte[]?` to be stored in the database.

`RestoreTraceContext` takes a `byte[]?` and returns it as a `PropagationContext?`, which you can use as the parent context when starting a new activity in your producer.
