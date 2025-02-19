using Testcontainers.MongoDb;

namespace YakShaveFx.OutboxKit.MongoDb.Tests;

// in xunit 3, we'll be able to use assembly fixtures to share the container across all tests
// until then, we'll have to use a collection fixture (though this means the tests don't run in parallel)
[CollectionDefinition(Name)]
public sealed class MongoDbCollection : ICollectionFixture<MongoDbFixture>
{
    public const string Name = "MongoDB collection";

    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}

// ReSharper disable once ClassNeverInstantiated.Global - it's instantiated by xUnit
public sealed class MongoDbFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _container = new MongoDbBuilder()
        .WithImage("mongo:7.0")
        .WithUsername("mongo")
        .WithPassword("mongo")
        // need replica set for change streams
        .WithReplicaSet()
        .Build();

    // besides setting the replica set, we also need to use the direct connection option,
    // otherwise the client will try to connect to the advertised host and port
    // which don't match what Testcontainers maps (to avoid port conflicts)
    // (note: it will be fixed in a future release of Testcontainers MongoDB integration,
    // in which case we'll be able to return the _container connection string directly)
    public string ConnectionString => $"{_container.GetConnectionString()}?replicaSet=rs0&directConnection=true";

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();
}