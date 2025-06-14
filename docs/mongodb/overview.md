---
outline: deep
---

# MongoDB provider overview

Installing:

```sh
dotnet add package YakShaveFx.OutboxKit.MongoDb
```

The MongoDB provider is, as the name implies, a provider to have OutboxKit work with MongoDB databases. It uses [MongoDB.Driver](https://www.mongodb.com/docs/drivers/csharp/current/) to interact with the database, with no other dependencies.

The provider currently implements only the polling approach.
