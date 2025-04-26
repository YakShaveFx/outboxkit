using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Driver;
using YakShaveFx.OutboxKit.MongoDb.Synchronization;
using static System.Threading.CancellationToken;

namespace YakShaveFx.OutboxKit.MongoDb.Tests.Synchronization;

[Collection(MongoDbCollection.Name)]
public class DistributedLockThingyTests(MongoDbFixture fixture)
{
    private readonly ILogger<DistributedLockThingy> _logger = NullLogger<DistributedLockThingy>.Instance;

    private readonly string _databaseName = $"test_{Guid.NewGuid():N}";

    [Fact]
    public async Task WhenAcquiringAvailableLockThenItsAcquired()
    {
        var database = GetDatabase();
        var settings = new DistributedLockSettings { ChangeStreamsEnabled = false };
        var timeProvider = new FakeTimeProvider();
        var sut = new DistributedLockThingy(settings, database, timeProvider, _logger);
        var lockDef = CreateLockDefinition();

        await using var @lock = await sut.AcquireAsync(lockDef, None);

        @lock.Should().NotBeNull();
        var doc = await GetLockDocument(lockDef.Id);
        doc.Should().NotBeNull();
        doc!.Owner.Should().Be(lockDef.Owner);
    }

    [Fact]
    public async Task WhenAcquiringUnavailableLockTheItWaitsForExpiration()
    {
        var settings = new DistributedLockSettings { ChangeStreamsEnabled = false };
        // so we can control time separately and test expiration
        var sut1TimeProvider = new FakeTimeProvider();
        var sut2TimeProvider = new FakeTimeProvider();
        var sut1 = new DistributedLockThingy(settings, GetDatabase(), sut1TimeProvider, _logger);
        var sut2 = new DistributedLockThingy(settings, GetDatabase(), sut2TimeProvider, _logger);
        var lockDef = CreateLockDefinition();

        // acquire first lock
        await using var @lock = await sut1.AcquireAsync(lockDef, None);

        // start acquiring second lock
        var secondLockTask = sut2.AcquireAsync(lockDef.WithDifferentOwner(), None);

        await Task.Delay(TimeSpan.FromSeconds(5));

        secondLockTask.Status.Should().NotBe(TaskStatus.RanToCompletion);

        // advance time for sut2 past expiration
        sut2TimeProvider.Advance(lockDef.Duration + TimeSpan.FromSeconds(1));

        // second lock should now acquire
        await using var secondLock = await secondLockTask.WaitAsync(TimeSpan.FromSeconds(5));
        secondLock.Should().NotBeNull();
    }

    [Fact]
    public async Task WhenAcquiringUnavailableLockTheItWaitsForChangeStreamsNotification()
    {
        var settings = new DistributedLockSettings { ChangeStreamsEnabled = true };
        var timeProvider = new FakeTimeProvider();
        var sut1 = new DistributedLockThingy(settings, GetDatabase(), timeProvider, _logger);
        var sut2 = new DistributedLockThingy(settings, GetDatabase(), timeProvider, _logger);
        var lockDef = CreateLockDefinition();
        var acquireLock1Tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLock1Tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // acquire first lock (in a task, so other tasks can run in parallel)
        var firstLockTask = Task.Run(async () =>
        {
            await using var lock1 = await sut1.AcquireAsync(lockDef, None);
            acquireLock1Tcs.SetResult();
            await releaseLock1Tcs.Task;
        });

        // give some time for the first lock to acquire
        await acquireLock1Tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // start acquiring second lock
        var secondLockTask = sut2.AcquireAsync(lockDef.WithDifferentOwner().WithoutOnLockLost(), None);

        // give sut2 some time to start acquiring the lock (subscribe to change stream)
        await Task.Delay(TimeSpan.FromSeconds(5));

        // release first lock
        releaseLock1Tcs.SetResult();

        // second lock should acquire immediately due to change stream notification
        await using var secondLock = await secondLockTask.WaitAsync(TimeSpan.FromSeconds(5));
        secondLock.Should().NotBeNull();
        await firstLockTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task WhenTryAcquireUnavailableLockThenItReturnsNull()
    {
        var settings = new DistributedLockSettings { ChangeStreamsEnabled = false };
        var timeProvider = new FakeTimeProvider();
        var sut1 = new DistributedLockThingy(settings, GetDatabase(), timeProvider, _logger);
        var sut2 = new DistributedLockThingy(settings, GetDatabase(), timeProvider, _logger);
        var lockDef = CreateLockDefinition();

        // acquire first lock
        await using var firstLock = await sut1.AcquireAsync(lockDef, None);

        // try to acquire second lock
        var secondLock = await sut2.TryAcquireAsync(lockDef.WithDifferentOwner(), None);
        secondLock.Should().BeNull();
    }

    [Fact]
    public async Task WhenTryAcquireWithConcurrentAttemptsThenOnlyOneSucceeds()
    {
        var settings = new DistributedLockSettings { ChangeStreamsEnabled = false };
        var timeProvider = new FakeTimeProvider();
        var lockId = Guid.NewGuid().ToString();
        var attempts = Enumerable.Range(0, 3)
            .Select(_ => new DistributedLockDefinition
            {
                Id = lockId,
                Owner = Guid.NewGuid().ToString(),
                Context = "Test",
                Duration = TimeSpan.FromSeconds(30)
            });

        var acquiredLocks = await Task.WhenAll(
            attempts.Select(def =>
            {
                var sut = new DistributedLockThingy(settings, GetDatabase(), timeProvider, _logger);
                return sut.TryAcquireAsync(def, None);
            }));

        acquiredLocks.Count(l => l is not null).Should().Be(1);
    }

    [Fact]
    public async Task WhenLockIsLostThenItsDetectedOnNextKeepAlive()
    {
        var settings = new DistributedLockSettings { ChangeStreamsEnabled = false };
        // so we can control time separately and test expiration
        var sut1TimeProvider = new FakeTimeProvider();
        var sut2TimeProvider = new FakeTimeProvider();
        var sut1 = new DistributedLockThingy(settings, GetDatabase(), sut1TimeProvider, _logger);
        var sut2 = new DistributedLockThingy(settings, GetDatabase(), sut2TimeProvider, _logger);
        var lockLostCalled = false;
        var lockDef = CreateLockDefinition() with
        {
            OnLockLost = () =>
            {
                lockLostCalled = true;
                return Task.CompletedTask;
            }
        };

        // acquire first lock
        await using var @lock = await sut1.AcquireAsync(lockDef, None);

        // advance time past expiration
        sut2TimeProvider.Advance(lockDef.Duration + TimeSpan.FromSeconds(1));

        // try to acquire with different owner
        await using var newLock = await sut2.TryAcquireAsync(lockDef.WithDifferentOwner(), None);

        // advance time past expiration, so it detects the lock is lost
        sut1TimeProvider.Advance(lockDef.Duration + TimeSpan.FromSeconds(1));

        // give some time for the lock lost callback to be called
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Assert
        newLock.Should().NotBeNull();
        lockLostCalled.Should().BeTrue();
    }

    [Fact]
    public async Task WhenLockIsLostThenItsDetectedByChangeStreams()
    {
        var settings = new DistributedLockSettings { ChangeStreamsEnabled = true };
        var timeProvider = new FakeTimeProvider();
        var sut = new DistributedLockThingy(settings, GetDatabase(), timeProvider, _logger);
        var lockLostTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lock1AcquiredTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lockDef = CreateLockDefinition() with
        {
            OnLockLost = () =>
            {
                lockLostTcs.SetResult();
                return Task.CompletedTask;
            }
        };

        // acquire first lock (in a task, so other tasks can run in parallel)
        var lock1Task = Task.Run(async () =>
        {
            await using var @lock = await sut.AcquireAsync(lockDef with { Context = "sut1" }, None);
            lock1AcquiredTcs.SetResult();
            await lockLostTcs.Task;
        });

        // give some time for the lock1Task to acquire the lock
        await lock1AcquiredTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var result = await ReplaceOwner(lockDef);

        result.ModifiedCount.Should().Be(1);
        await lockLostTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await lock1Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task WhenLockIsDeletedExterallyThenItsDetectedByChangeStreamsAndReacquired()
    {
        var settings = new DistributedLockSettings { ChangeStreamsEnabled = true };
        var timeProvider = new FakeTimeProvider();
        var sut = new DistributedLockThingy(settings, GetDatabase(), timeProvider, _logger);
        var lockLostCalled = false;
        var lockDef = CreateLockDefinition() with
        {
            OnLockLost = () =>
            {
                lockLostCalled = true;
                return Task.CompletedTask;
            }
        };

        // acquire lock
        await using var @lock = await sut.AcquireAsync(lockDef, None);

        // simulate external modification by directly updating the database
        var deleteResult = await DeleteLock(lockDef);

        // give some time for the change stream to detect and act on the modification
        await Task.Delay(TimeSpan.FromSeconds(1));

        var lockExists = await LockExists(lockDef);

        // Assert
        deleteResult.DeletedCount.Should().Be(1);
        lockLostCalled.Should().BeFalse();
        lockExists.Should().BeTrue();
    }
    
    /*
     * The tests
     * - WhenAcquiringAndReleasingLockThenItsReleased
     * - WhenAcquiringAndReleasingLockWithChangeStreamsEnabledThenItsReleased
     * Were added because of a bug where the lock was disposed, but the keep alive kept running, reacquiring the lock.
     * This is why they might feel very specific, as they're testing the conditions that caused the bug.
     */
    
    [Fact]
    public async Task WhenAcquiringAndReleasingLockThenItsReleased()
    {
        var database = GetDatabase();
        var settings = new DistributedLockSettings { ChangeStreamsEnabled = false };
        var timeProvider = TimeProvider.System;
        var sut = new DistributedLockThingy(settings, database, timeProvider, _logger);
        var lockDef = CreateLockDefinition() with { Duration = TimeSpan.FromSeconds(1) };

        await using var @lock = await sut.AcquireAsync(lockDef, None);
        await Task.Delay(TimeSpan.FromSeconds(1)); // give it a bit of time for the keep alive to kick in
        await @lock.DisposeAsync();
        // give it a bit of time for keep alive to run (if not cancelled)
        await Task.Delay(TimeSpan.FromSeconds(5)); 

        var doc = await GetLockDocument(lockDef.Id);
        doc.Should().BeNull();
    }

    [Fact]
    public async Task WhenAcquiringAndReleasingLockWithChangeStreamsEnabledThenItsReleased()
    {
        var database = GetDatabase();
        var settings = new DistributedLockSettings { ChangeStreamsEnabled = true };
        var timeProvider = new FakeTimeProvider();
        var sut = new DistributedLockThingy(settings, database, timeProvider, _logger);
        var lockDef = CreateLockDefinition();

        await using var @lock = await sut.AcquireAsync(lockDef, None);
        await Task.Delay(TimeSpan.FromSeconds(1)); // give it a bit of time for the keep alive to kick in
        await @lock.DisposeAsync();
        // give it a bit of time for change streams to be signaled and the code to run (if not cancelled)
        // (using FakeTimeProvider but not touching it, so we see the change streams in action, instead of the delay)
        await Task.Delay(TimeSpan.FromSeconds(5));

        var doc = await GetLockDocument(lockDef.Id);
        doc.Should().BeNull();
    }

    // need to delete using a different client, otherwise it won't trigger change streams
    private async Task<DeleteResult> DeleteLock(DistributedLockDefinition lockDef)
        => await GetDatabase()
            .GetCollection()
            .DeleteOneAsync(Builders<DistributedLockDocument>.Filter.Eq(d => d.Id, lockDef.Id));

    private async Task<bool> LockExists(DistributedLockDefinition lockDef)
        => await GetDatabase()
            .GetCollection()
            .Find(Builders<DistributedLockDocument>.Filter.Eq(d => d.Id, lockDef.Id))
            .AnyAsync();

    private async Task<ReplaceOneResult> ReplaceOwner(DistributedLockDefinition lockDef)
        => await GetDatabase()
            .GetCollection()
            .ReplaceOneAsync(
                Builders<DistributedLockDocument>.Filter.Eq(d => d.Id, lockDef.Id),
                new DistributedLockDocument
                {
                    Id = lockDef.Id,
                    Owner = Guid.NewGuid().ToString(),
                    ExpiresAt = DateTimeOffset.UtcNow.Add(lockDef.Duration).ToUnixTimeMilliseconds()
                });

    private async Task<DistributedLockDocument?> GetLockDocument(string id)
        => await GetDatabase()
            .GetCollection()
            .Find(Builders<DistributedLockDocument>.Filter.Eq(d => d.Id, id))
            .FirstOrDefaultAsync();

    private static DistributedLockDefinition CreateLockDefinition() => new()
    {
        Id = Guid.NewGuid().ToString(),
        Owner = Guid.NewGuid().ToString(),
        Context = "Test",
        Duration = TimeSpan.FromSeconds(30)
    };

    private IMongoDatabase GetDatabase()
        => new MongoClient(fixture.ConnectionString).GetDatabase(_databaseName);
}

file static class Extensions
{
    public static IMongoCollection<DistributedLockDocument> GetCollection(this IMongoDatabase db)
        => db.GetCollection<DistributedLockDocument>(DistributedLockSettings.DefaultCollectionName);

    public static DistributedLockDefinition WithDifferentOwner(this DistributedLockDefinition def)
        => def with { Owner = Guid.NewGuid().ToString() };

    public static DistributedLockDefinition WithoutOnLockLost(this DistributedLockDefinition def)
        => def with { OnLockLost = () => Task.CompletedTask };
}