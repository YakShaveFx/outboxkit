---
outline: deep
---

# PostgreSQL provider overview

Installing:

```sh
dotnet add package YakShaveFx.OutboxKit.PostgreSql
```

The PostgreSQL provider is, as the name implies, a provider to have OutboxKit work with PostgreSQL databases. It uses [Npgsql](https://www.npgsql.org) to interact with the database, with not other dependencies (i.e. no EF Core, no Dapper, etc).

The provider currently implements only the polling approach.
