using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.Core.Arguments;
using YakShaveFx.OutboxKit.Core.OpenTelemetry;
using YakShaveFx.OutboxKit.Core.Polling;
using static YakShaveFx.OutboxKit.Core.Tests.OpenTelemetryHelpers;

namespace YakShaveFx.OutboxKit.Core.Tests.Polling;

public class PollingProducerTests
{
    private static readonly OutboxKey Key = new("sample-provider", "some-key");
    private static readonly PollingProducerMetrics Metrics = new(CreateMeterFactoryStub());
    private static readonly NullLogger<PollingProducer> Logger = NullLogger<PollingProducer>.Instance;

    private static readonly ICompletionRetryCollector CompleteRetryStub = new CompleteRetryRetryCollectorStub();

    [Fact]
    public async Task WhenBatchIsEmptyThenProducerIsNotInvoked()
    {
        var producerSpy = CreateProducer();
        var fetcherStub = new BatchFetcherStub([new BatchContextStub([], false)]);
        var sut = new PollingProducer(Key, fetcherStub, producerSpy, CompleteRetryStub, Metrics, Logger);

        await sut.ProducePendingAsync(CancellationToken.None);

        await producerSpy
            .DidNotReceive()
            .ProduceAsync(Arg.Any<OutboxKey>(), Arg.Any<IReadOnlyCollection<IMessage>>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task WhileThereAreAvailableBatchesProducerIsInvoked(int numberOfBatches)
    {
        var producerSpy = CreateProducer();
        var fetcherStub = new BatchFetcherStub(CreateBatchContexts(numberOfBatches));
        var sut = new PollingProducer(Key, fetcherStub, producerSpy, CompleteRetryStub, Metrics, Logger);

        await sut.ProducePendingAsync(CancellationToken.None);

        await producerSpy
            .Received(numberOfBatches)
            .ProduceAsync(Arg.Any<OutboxKey>(), Arg.Any<IReadOnlyCollection<IMessage>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenCompletingBatchThenMessagesAreCollectedForRetry()
    {
        var producerSpy = CreateProducer();
        var fetcherStub = new BatchFetcherStub([new BatchContextStub([new MessageStub()], false, true)]);
        var collectorSpy = Substitute.For<ICompletionRetryCollector>();
        var sut = new PollingProducer(Key, fetcherStub, producerSpy, collectorSpy, Metrics, Logger);

        await sut.ProducePendingAsync(CancellationToken.None);

        collectorSpy
            .Received(1)
            .Collect(Arg.Any<IReadOnlyCollection<IMessage>>());
    }

    private static IBatchContext[] CreateBatchContexts(int numberOfBatches)
        =>
        [
            .. Enumerable.Range(0, numberOfBatches)
                .Select(i => new BatchContextStub([new MessageStub()], i + 1 < numberOfBatches))
        ];

    private static IBatchProducer CreateProducer()
    {
        var producerSpy = Substitute.For<IBatchProducer>();
        producerSpy
            .ProduceAsync(Arg.Any<OutboxKey>(), Arg.Any<IReadOnlyCollection<IMessage>>(), Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(args =>
                Task.FromResult(new BatchProduceResult { Ok = (IReadOnlyCollection<IMessage>)args[1] }));
        return producerSpy;
    }
}

file sealed class MessageStub : IMessage;

file sealed class BatchFetcherStub(IBatchContext[] contexts) : IBatchFetcher
{
    private int _index = 0;

    public Task<IBatchContext> FetchAndHoldAsync(CancellationToken ct)
        => _index >= contexts.Length
            ? Task.FromResult<IBatchContext>(EmptyBatchContext.Instance)
            : Task.FromResult(contexts[_index++]);
}

file sealed class BatchContextStub(IReadOnlyCollection<IMessage> messages, bool hasNext, bool throwOnComplete = false)
    : IBatchContext
{
    public IReadOnlyCollection<IMessage> Messages => messages;

    public Task CompleteAsync(IReadOnlyCollection<IMessage> ok, CancellationToken ct) => !throwOnComplete
        ? Task.CompletedTask
        : throw new Exception("Simulated failure on complete");

    public Task<bool> HasNextAsync(CancellationToken ct) => Task.FromResult(hasNext);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

file sealed class CompleteRetryRetryCollectorStub : ICompletionRetryCollector
{
    public void Collect(IReadOnlyCollection<IMessage> messages)
    {
    }
}