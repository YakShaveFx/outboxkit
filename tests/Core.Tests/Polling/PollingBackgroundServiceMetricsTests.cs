using System.Diagnostics.Metrics;
using FluentAssertions;
using YakShaveFx.OutboxKit.Core.OpenTelemetry;
using YakShaveFx.OutboxKit.Core.Polling;

namespace YakShaveFx.OutboxKit.Core.Tests.Polling;

public class PollingBackgroundServiceMetricsTests
{
    // this is less of test of correctness, and more to make sure that if new types of results are added, we don't forget to update the metrics
    [Fact]
    public void WhenReportingCycleExecutedThenAllResultsAreSupported()
    {
        const string instrumentName = MetricsConstants.PollingBackgroundService.PollingCycles.Name;
        const string tagKey = MetricsConstants.PollingBackgroundService.PollingCycles.Tags.Result;
        const string providerKeyTagKey = MetricsConstants.Shared.Tags.ProviderKeyTag;
        const string providerKey = nameof(WhenReportingCycleExecutedThenAllResultsAreSupported);
        const string clientKey = nameof(WhenReportingCycleExecutedThenAllResultsAreSupported);
        
        List<ProducePendingResult> resultsReported = [];
        var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Name == instrumentName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        meterListener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            var tagsArray = tags.ToArray();
            var providerKeyTag = tagsArray.Single(t => t.Key == providerKeyTagKey);
            
            if (!providerKey.Equals(providerKeyTag.Value)) return;

            var resultTag = tagsArray.Single(t => t.Key == tagKey);
            resultsReported.Add(Enum.Parse<ProducePendingResult>(resultTag.Value!.ToString()!));

        });
        meterListener.Start();

        using var sut = new PollingBackgroundServiceMetrics(OpenTelemetryHelpers.CreateMeterFactoryStub());
        var possibleResults = Enum.GetValues<ProducePendingResult>();
        foreach (var result in possibleResults)
        {
            sut
                .Invoking(s => s.PollingCycleExecuted(new(providerKey, clientKey), result))
                .Should()
                .NotThrow();
        }

        resultsReported.Count.Should().Be(possibleResults.Length);
        resultsReported.Should().BeEquivalentTo(possibleResults);
    }
}