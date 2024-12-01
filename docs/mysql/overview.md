---
outline: deep
---

# MySQL provider overview

Installing:

```sh
dotnet add package YakShaveFx.OutboxKit.MySql
```

The MySQL provider is, as the name implies, a provider to have OutboxKit work with MySQL databases. It uses [MySqlConnector](https://mysqlconnector.net) to interact with the database, with not other dependencies (i.e. no EF Core, no Dapper, etc).

The provider currently implements only the polling approach.