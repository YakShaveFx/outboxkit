# OutboxKit

![OutboxKit logo](logo/outboxkit-128.png)

The goal of OutboxKit is to provide foundational features to assist in implementing the [transactional outbox pattern](https://blog.codingmilitia.com/2020/04/13/aspnet-040-from-zero-to-overkill-event-driven-integration-transactional-outbox-pattern/).

- Core [![NuGet](https://img.shields.io/nuget/v/YakShaveFx.OutboxKit.Core.svg)](https://www.nuget.org/packages/YakShaveFx.OutboxKit.Core/)
[![Feedz.io ("nightly")](https://img.shields.io/badge/endpoint.svg?url=https%3A%2F%2Ff.feedz.io%2Fyakshavefx%2Foutboxkit%2Fshield%2FYakShaveFx.OutboxKit.Core%2Flatest&label=Feedz.io%20%28%22nightly%22%29)](https://f.feedz.io/yakshavefx/outboxkit/packages/YakShaveFx.OutboxKit.Core/latest/download)
- MySQL [![NuGet](https://img.shields.io/nuget/v/YakShaveFx.OutboxKit.MySql.svg)](https://www.nuget.org/packages/YakShaveFx.OutboxKit.MySql/)
[![Feedz.io ("nightly")](https://img.shields.io/badge/endpoint.svg?url=https%3A%2F%2Ff.feedz.io%2Fyakshavefx%2Foutboxkit%2Fshield%2FYakShaveFx.OutboxKit.MySql%2Flatest&label=Feedz.io%20%28%22nightly%22%29)](https://f.feedz.io/yakshavefx/outboxkit/packages/YakShaveFx.OutboxKit.MySql/latest/download)

> You can add the following [Feedz.io](https://feedz.io) source to your NuGet configuration to get the nightly builds: `https://f.feedz.io/yakshavefx/outboxkit/nuget/index.json`

The idea is that the library is:

- somewhat generic - in order to accommodate different technologies, such as message brokers, databases and database access libraries
- opinionated - trying to solve the kinds of problems I'm facing (and have faced in the past), not everyone's problems
- unambitious - again, trying to solve a specific set of problems, so it fits my current needs in terms of features and performance, but might not fit yours

## Why use this?

You probably shouldn't 😅. If possible, using more comprehensive libraries like Wolverine, NServiceBus, Brighter, etc, is probably a better idea, as they give a more integrated messaging experience (plus they've been at this for much longer, and in a wider range of scenarios). This toolkit is aimed at scenarios where greater degree of customization of the whole messaging approach is wanted.

## Docs

Coming soon 👷‍♀️

## Misc

Logo by [@khalidabuhakmeh](https://github.com/khalidabuhakmeh)