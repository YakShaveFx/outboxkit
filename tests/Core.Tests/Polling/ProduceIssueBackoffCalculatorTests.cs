using FluentAssertions;
using YakShaveFx.OutboxKit.Core.Polling;

namespace YakShaveFx.OutboxKit.Core.Tests.Polling;

public class ProduceIssueBackoffCalculatorTests
{
    [Fact]
    internal void WhenCalculatingForUnhandledExceptionThenItIncreasesWithEachCallUntilALimit()
    {
        var sut = new ProduceIssueBackoffCalculator();
        
        var backoffCalculations = new[]
        {
            sut.CalculateForUnhandledException(),
            sut.CalculateForUnhandledException(),
            sut.CalculateForUnhandledException(),
            sut.CalculateForUnhandledException(),
            sut.CalculateForUnhandledException()
        };

        backoffCalculations[0].Should().BeLessThan(backoffCalculations[1]);
        backoffCalculations[1].Should().BeLessThan(backoffCalculations[2]);
        backoffCalculations[2].Should().BeLessThan(backoffCalculations[3]);
        backoffCalculations[3].Should().BeLessThan(backoffCalculations[4]);
    }
    
    [Theory]
    [InlineData(ProducePendingResult.FetchError)]
    [InlineData(ProducePendingResult.ProduceError)]
    [InlineData(ProducePendingResult.PartialProduction)]
    internal void WhenCalculatingForResultThenItIncreasesWithEachCallUntilALimit(ProducePendingResult result)
    {
        var sut = new ProduceIssueBackoffCalculator();
        
        var backoffCalculations = new[]
        {
            sut.CalculateForResult(result),
            sut.CalculateForResult(result),
            sut.CalculateForResult(result),
            sut.CalculateForResult(result),
            sut.CalculateForResult(result)
        };

        backoffCalculations[0].Should().BeLessThan(backoffCalculations[1]);
        backoffCalculations[1].Should().BeLessThan(backoffCalculations[2]);
        backoffCalculations[2].Should().BeLessThan(backoffCalculations[3]);
        backoffCalculations[3].Should().BeLessThan(backoffCalculations[4]);
    }
    
    [Theory]
    [InlineData(ProducePendingResult.Ok)]
    [InlineData(ProducePendingResult.CompleteError)]
    internal void WhenCalculatingForUnexpectedResultThenItThrows(ProducePendingResult result)
    {
        var sut = new ProduceIssueBackoffCalculator();
        
        var act = () => sut.CalculateForResult(result);
        
        act
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"Unexpected {nameof(ProducePendingResult)} {result}");
    }
    
    [Fact]
    internal void WhenCalculatingForDifferentResultsOrExceptionThenItResetsTheOccurrences()
    {
        var sut = new ProduceIssueBackoffCalculator();
        
        var backoffCalculations = new[]
        {
            sut.CalculateForResult(ProducePendingResult.FetchError),
            sut.CalculateForResult(ProducePendingResult.ProduceError),
            sut.CalculateForResult(ProducePendingResult.PartialProduction),
            sut.CalculateForUnhandledException(),
        };

        backoffCalculations[0].Should().Be(backoffCalculations[1]);
        backoffCalculations[1].Should().Be(backoffCalculations[2]);
        backoffCalculations[2].Should().Be(backoffCalculations[3]);
    }
    
    [Theory]
    [InlineData(ProducePendingResult.FetchError)]
    [InlineData(ProducePendingResult.ProduceError)]
    [InlineData(ProducePendingResult.PartialProduction)]
    internal void WhenResettingBetweenResultCalculationsThenItResetsTheOccurrences(ProducePendingResult result)
    {
        var sut = new ProduceIssueBackoffCalculator();
        
        var firstBackoff = sut.CalculateForResult(result);
        sut.Reset();
        var secondBackoff = sut.CalculateForResult(result);

        firstBackoff.Should().Be(secondBackoff);
    }
    
    [Fact]
    internal void WhenResettingBetweenExceptionCalculationsThenItResetsTheOccurrences()
    {
        var sut = new ProduceIssueBackoffCalculator();
        
        var firstBackoff = sut.CalculateForUnhandledException();
        sut.Reset();
        var secondBackoff = sut.CalculateForUnhandledException();

        firstBackoff.Should().Be(secondBackoff);
    }
}