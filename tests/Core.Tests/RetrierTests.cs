using FluentAssertions;

namespace YakShaveFx.OutboxKit.Core.Tests;

public class RetrierTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task WhenRetryingForeverThenOnlyReturnsOnSuccess()
    {
        var sut = new RetrierBuilderFactory(TimeProvider.System)
            .Create()
            .WithMaxRetries(int.MaxValue)
            .WithDelayCalculator(_ => TimeSpan.FromMilliseconds(10))
            .WithShouldRetryDecider(_ => true)
            .Build();

        const int maxAttempts = 10;
        var attempts = 0;
        var retryTask = sut.ExecuteWithRetryAsync(() =>
            {
                attempts++;
                if (attempts < maxAttempts) throw new Exception("Failed attempt");
                return Task.CompletedTask;
            },
            _ct);
        
        await Task.Delay(TimeSpan.FromMilliseconds(maxAttempts * 10), _ct); // wait for all attempts to be made
        
        await retryTask; // should complete successfully

        attempts.Should().Be(maxAttempts);
    }
    
    [Fact]
    public async Task WhenRetryingWithLimitedAttemptsThenThrowsWhenRetriesExhausted()
    {
        const int maxAttempts = 3;
        var sut = new RetrierBuilderFactory(TimeProvider.System)
            .Create()
            .WithMaxRetries(maxAttempts - 1) // -1 because the first attempt is not considered a retry
            .WithDelayCalculator(_ => TimeSpan.FromMilliseconds(10))
            .WithShouldRetryDecider(_ => true)
            .Build();

        
        var attempts = 0;
        var act = () => sut.ExecuteWithRetryAsync(() =>
            {
                attempts++;
                throw new Exception("Failed attempt");
            },
            _ct);
        
        await act.Should().ThrowAsync<Exception>().WithMessage("Failed attempt");
        attempts.Should().Be(maxAttempts);
    }
    
    [Fact]
    public async Task WhenExceptionIsNotRetryableThenThrowsImmediately()
    {
        var sut = new RetrierBuilderFactory(TimeProvider.System)
            .Create()
            .WithMaxRetries(3)
            .WithDelayCalculator(_ => TimeSpan.FromMilliseconds(10))
            .WithShouldRetryDecider(_ => false)
            .Build();

        var attempts = 0;
        var act = () => sut.ExecuteWithRetryAsync(
            () =>
            {
                attempts++;
                throw new Exception("Non-retryable exception");
            },
            _ct);
        
        await act.Should().ThrowAsync<Exception>().WithMessage("Non-retryable exception");
        attempts.Should().Be(1); // should not retry at all
    }
}