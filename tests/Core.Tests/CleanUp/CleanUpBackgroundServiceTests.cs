using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using YakShaveFx.OutboxKit.Core.CleanUp;
using YakShaveFx.OutboxKit.Core.OpenTelemetry;

namespace YakShaveFx.OutboxKit.Core.Tests.CleanUp;

public class CleanUpBackgroundServiceTests
{
    private const string Key = "key";
    private static readonly NullLogger<CleanUpBackgroundService> Logger = NullLogger<CleanUpBackgroundService>.Instance;
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly CoreCleanUpSettings _settings = new();

    [Fact]
    public async Task WhenServiceStartsTheCleanerIsInvoked()
    {
        var cleanerSpy = Substitute.For<IOutboxCleaner>();
        var services = CreateServices(cleanerSpy);
        var sut = new CleanUpBackgroundService(
            Key,
            _timeProvider,
            _settings,
            services.GetRequiredService<CleanerMetrics>(),
            services.GetRequiredService<IServiceScopeFactory>(),
            Logger);
        
        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(10)); // give it a bit to run and block
        
        await cleanerSpy.Received(1).CleanAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UntilIntervalIsReachedTheCleanerIsNotInvokedAgain()
    {
        var cleanerSpy = Substitute.For<IOutboxCleaner>();
        var services = CreateServices(cleanerSpy);
        var sut = new CleanUpBackgroundService(
            Key,
            _timeProvider,
            _settings,
            services.GetRequiredService<CleanerMetrics>(),
            services.GetRequiredService<IServiceScopeFactory>(),
            Logger);
        
        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(10)); // give it a bit to run and block
        cleanerSpy.ClearReceivedCalls(); // ignore startup call

        _timeProvider.Advance(_settings.CleanUpInterval - TimeSpan.FromMilliseconds(1));
        await Task.Delay(TimeSpan.FromMilliseconds(10)); // give it a bit to run again

        await cleanerSpy.Received(0).CleanAsync(Arg.Any<CancellationToken>());
    }
    
    [Fact]
    public async Task WhenIntervalIsReachedTheCleanerIsInvokedAgain()
    {
        var cleanerSpy = Substitute.For<IOutboxCleaner>();
        var services = CreateServices(cleanerSpy);
        var sut = new CleanUpBackgroundService(
            Key,
            _timeProvider,
            _settings,
            services.GetRequiredService<CleanerMetrics>(),
            services.GetRequiredService<IServiceScopeFactory>(),
            Logger);
        
        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(10)); // give it a bit to run and block
        cleanerSpy.ClearReceivedCalls(); // ignore startup call

        _timeProvider.Advance(_settings.CleanUpInterval);
        await Task.Delay(TimeSpan.FromMilliseconds(10)); // give it a bit to run again

        await cleanerSpy.Received(1).CleanAsync(Arg.Any<CancellationToken>());
    }
    
    private static IServiceProvider CreateServices(IOutboxCleaner cleaner)
        => new ServiceCollection()
            .AddKeyedSingleton(Key, cleaner)
            .AddMetrics()
            .AddSingleton<CleanerMetrics>()
            .BuildServiceProvider();
}