using Testcontainers.MongoDb;
using YakShaveFx.OutboxKit.MongoDb.Tests;

[assembly: AssemblyFixture(typeof(MongoDbFixture))]

namespace YakShaveFx.OutboxKit.MongoDb.Tests;

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
    
    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}