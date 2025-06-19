namespace YakShaveFx.OutboxKit.Core;

// very basic implementation of retry logic, to avoid taking in external dependencies, otherwise would just use Polly

// would love to have a less hideous name, but can't think of one right now
internal sealed class RetrierBuilderFactory(TimeProvider timeProvider)
{
    public RetrierBuilder Create() => new(timeProvider);
}

internal sealed class RetrierBuilder(TimeProvider timeProvider)
{
    private int _maxRetries = int.MaxValue;

    private Func<int, TimeSpan> _delayCalculator
        = retries => retries switch
        {
            1 => TimeSpan.FromSeconds(1),
            2 => TimeSpan.FromSeconds(5),
            3 => TimeSpan.FromSeconds(30),
            4 => TimeSpan.FromMinutes(1),
            _ => TimeSpan.FromMinutes(5)
        };

    private Func<Exception, bool> _shouldRetryDecider = _ => true;

    public RetrierBuilder WithMaxRetries(int maxRetries)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRetries, 1);
        _maxRetries = maxRetries;
        return this;
    }

    public RetrierBuilder WithDelayCalculator(Func<int, TimeSpan> delayCalculator)
    {
        ArgumentNullException.ThrowIfNull(delayCalculator);
        _delayCalculator = delayCalculator;
        return this;
    }

    public RetrierBuilder WithShouldRetryDecider(Func<Exception, bool> shouldRetry)
    {
        ArgumentNullException.ThrowIfNull(shouldRetry);
        _shouldRetryDecider = shouldRetry;
        return this;
    }

    public Retrier Build() => new(_maxRetries, _delayCalculator, _shouldRetryDecider, timeProvider);
}

internal sealed class Retrier(
    int maxRetries,
    Func<int, TimeSpan> delayCalculator,
    Func<Exception, bool> shouldRetryDecider,
    TimeProvider timeProvider)
{
    public async Task ExecuteWithRetryAsync<TArgs>(Func<TArgs, Task> action, TArgs args, CancellationToken ct)
    {
        var retries = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await action(args);
                return;
            }
            catch (Exception ex) when (shouldRetryDecider(ex))
            {
                if (retries >= maxRetries) throw;
                
                ++retries;
                
                await Task.Delay(delayCalculator(retries), timeProvider, ct);
            }
        }
    }

    public Task ExecuteWithRetryAsync(Func<Task> action, CancellationToken ct)
        => ExecuteWithRetryAsync(static action => action(), action, ct);
}