namespace YakShaveFx.OutboxKit.Core.Polling;

internal sealed class ProduceIssueBackoffCalculator
{
    private int _unhandledExceptionOccurrences;
    private int _lastResultOccurrences;
    private ProducePendingResult? _lastResult;

    public void Reset()
    {
        ResetUnhandledException();
        ResetResult();
    }

    public TimeSpan CalculateForUnhandledException()
    {
        ResetResult();
        ++_unhandledExceptionOccurrences;
        return BackoffForOccurrences(_unhandledExceptionOccurrences);
    }

    public TimeSpan CalculateForResult(ProducePendingResult result)
    {
        ResetUnhandledException();

        if (result == _lastResult) ++_lastResultOccurrences;
        else _lastResultOccurrences = 1;
        _lastResult = result;

        return result switch
        {
            ProducePendingResult.FetchError => BackoffForOccurrences(_lastResultOccurrences),
            ProducePendingResult.ProduceError => BackoffForOccurrences(_lastResultOccurrences),
            ProducePendingResult.PartialProduction => BackoffForOccurrences(_lastResultOccurrences),
            _ => ThrowOnUnexpectedResult(result)
        };

        static TimeSpan ThrowOnUnexpectedResult(ProducePendingResult result)
            => throw new InvalidOperationException($"Unexpected {nameof(ProducePendingResult)} {result}");
    }

    private void ResetUnhandledException() => _unhandledExceptionOccurrences = 0;

    private void ResetResult()
    {
        _lastResultOccurrences = 0;
        _lastResult = null;
    }

    private static TimeSpan BackoffForOccurrences(int occurrences)
        => occurrences switch
        {
            1 => TimeSpan.FromSeconds(1),
            2 => TimeSpan.FromSeconds(5),
            3 => TimeSpan.FromSeconds(30),
            4 => TimeSpan.FromMinutes(1),
            _ => TimeSpan.FromMinutes(5)
        };
}