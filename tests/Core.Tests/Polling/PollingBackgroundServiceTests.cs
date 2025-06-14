using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using YakShaveFx.OutboxKit.Core.Polling;

namespace YakShaveFx.OutboxKit.Core.Tests.Polling;

public class PollingBackgroundServiceTests
{
    private static readonly OutboxKey Key = new("sample-provider", "some-key");
    private static readonly NullLogger<PollingBackgroundService> Logger = NullLogger<PollingBackgroundService>.Instance;
    private readonly Listener _listener = new();
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly CorePollingSettings _settings = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task WhenServiceStartsTheProducerIsInvoked()
    {
        var producerSpy = Substitute.For<IPollingProducer>();
        var sut = new PollingBackgroundService(Key, _listener, producerSpy, _timeProvider, _settings, Logger);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(100), _ct); // give it a bit to run and block

        await producerSpy.Received(1).ProducePendingAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UntilPollingIntervalIsReachedTheProducerIsNotInvokedAgain()
    {
        var producerSpy = Substitute.For<IPollingProducer>();
        var sut = new PollingBackgroundService(Key, _listener, producerSpy, _timeProvider, _settings, Logger);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(100), _ct); // give it a bit to run and block
        producerSpy.ClearReceivedCalls(); // ignore startup call

        _timeProvider.Advance(_settings.PollingInterval - TimeSpan.FromMilliseconds(1));
        await Task.Delay(TimeSpan.FromMilliseconds(100), _ct); // give it a bit to run again

        await producerSpy.Received(0).ProducePendingAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenPollingIntervalIsReachedThenTheProducerIsInvokedAgain()
    {
        var producerSpy = Substitute.For<IPollingProducer>();
        var sut = new PollingBackgroundService(Key, _listener, producerSpy, _timeProvider, _settings, Logger);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(100), _ct); // give it a bit to run and block
        producerSpy.ClearReceivedCalls(); // ignore startup call

        _timeProvider.Advance(_settings.PollingInterval);
        await Task.Delay(TimeSpan.FromMilliseconds(100), _ct); // give it a bit to run again

        await producerSpy.Received(1).ProducePendingAsync(Arg.Any<CancellationToken>());
    }


    [Fact]
    public async Task WhenListenerIsTriggeredThenTheProducerIsInvokedAgain()
    {
        var producerSpy = Substitute.For<IPollingProducer>();
        var sut = new PollingBackgroundService(Key, _listener, producerSpy, _timeProvider, _settings, Logger);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(100), _ct); // give it a bit to run and block
        producerSpy.ClearReceivedCalls(); // ignore startup call

        _listener.OnNewMessages();
        await Task.Delay(TimeSpan.FromMilliseconds(100), _ct); // give it a bit to run again

        await producerSpy.Received(1).ProducePendingAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenCancellationTokenIsSignaledThenTheServiceStops()
    {
        var producerStub = Substitute.For<IPollingProducer>();
        var sut = new PollingBackgroundService(Key, _listener, producerStub, _timeProvider, _settings, Logger);

        var cts = new CancellationTokenSource();

        await sut.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(10), CancellationToken.None); // give it a bit to run and block

        await cts.CancelAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(10), CancellationToken.None); // give it a bit to run again

        sut.ExecuteTask.Should().BeEquivalentTo(new { IsCompleted = true, IsCompletedSuccessfully = true });
    }

    [Fact]
    public async Task WhenTheProducerThrowsTheServiceRemainsRunning()
    {
        var producerMock = Substitute.For<IPollingProducer>();
        producerMock
            .When(x => x.ProducePendingAsync(Arg.Any<CancellationToken>()))
            .Throw(new InvalidOperationException("test"));

        var sut = new PollingBackgroundService(Key, _listener, producerMock, _timeProvider, _settings, Logger);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(10), CancellationToken.None); // give it a bit to run and block

        sut.ExecuteTask.Should().BeEquivalentTo(new { IsCompleted = false });
    }
}