using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using YakShaveFx.OutboxKit.Core.CleanUp;
using YakShaveFx.OutboxKit.Core.OpenTelemetry;
using static YakShaveFx.OutboxKit.Core.Tests.OpenTelemetryHelpers;

namespace YakShaveFx.OutboxKit.Core.Tests.CleanUp;

public class CleanUpBackgroundServiceTests
{
    private static readonly OutboxKey Key = new("sample-provider", "some-key");
    private static readonly NullLogger<CleanUpBackgroundService> Logger = NullLogger<CleanUpBackgroundService>.Instance;
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly CoreCleanUpSettings _settings = new();
    private readonly CleanerBackgroundServiceMetrics _metrics = new(CreateMeterFactoryStub());
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task WhenServiceStartsTheCleanerIsInvoked()
    {
        var cleanerSpy = Substitute.For<IOutboxCleaner>();
        var sut = new CleanUpBackgroundService(
            Key,
            cleanerSpy,
            _timeProvider,
            _settings,
            _metrics,
            Logger);
        
        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(100), _ct); // give it a bit to run and block
        
        await cleanerSpy.Received(1).CleanAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UntilIntervalIsReachedTheCleanerIsNotInvokedAgain()
    {
        var cleanerSpy = Substitute.For<IOutboxCleaner>();
        var sut = new CleanUpBackgroundService(
            Key,
            cleanerSpy,
            _timeProvider,
            _settings,
            _metrics,
            Logger);
        
        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(100), _ct); // give it a bit to run and block
        cleanerSpy.ClearReceivedCalls(); // ignore startup call

        _timeProvider.Advance(_settings.CleanUpInterval - TimeSpan.FromMilliseconds(1));
        await Task.Delay(TimeSpan.FromMilliseconds(100), _ct); // give it a bit to run again

        await cleanerSpy.Received(0).CleanAsync(Arg.Any<CancellationToken>());
    }
    
    [Fact]
    public async Task WhenIntervalIsReachedTheCleanerIsInvokedAgain()
    {
        var cleanerSpy = Substitute.For<IOutboxCleaner>();
        var sut = new CleanUpBackgroundService(
            Key,
            cleanerSpy,
            _timeProvider,
            _settings,
            _metrics,
            Logger);
        
        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(100), _ct); // give it a bit to run and block
        cleanerSpy.ClearReceivedCalls(); // ignore startup call

        _timeProvider.Advance(_settings.CleanUpInterval);
        await Task.Delay(TimeSpan.FromMilliseconds(100), _ct); // give it a bit to run again

        await cleanerSpy.Received(1).CleanAsync(Arg.Any<CancellationToken>());
    }
}