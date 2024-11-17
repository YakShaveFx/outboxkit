using NSubstitute;
using YakShaveFx.OutboxKit.Core.OpenTelemetry;
using YakShaveFx.OutboxKit.Core.Polling;
using static YakShaveFx.OutboxKit.Core.Tests.OpenTelemetryHelpers;

namespace YakShaveFx.OutboxKit.Core.Tests.Polling;

public class PollingProducerTests
{
    private static readonly OutboxKey Key = new("sample-provider", "some-key");
    private static readonly ProducerMetrics Metrics = new(CreateMeterFactoryStub());

    [Fact]
    public async Task WhenBatchIsEmptyThenProducerIsNotInvoked()
    {
        var producerSpy = CreateProducer();
        var fetcherStub = new BatchFetcherStub([new BatchContextStub([], false)]);
        var sut = new PollingProducer(Key, fetcherStub, producerSpy, Metrics);

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
        var sut = new PollingProducer(Key, fetcherStub, producerSpy, Metrics);

        await sut.ProducePendingAsync(CancellationToken.None);

        await producerSpy
            .Received(numberOfBatches)
            .ProduceAsync(Arg.Any<OutboxKey>(), Arg.Any<IReadOnlyCollection<IMessage>>(), Arg.Any<CancellationToken>());
    }

    private static BatchContextStub[] CreateBatchContexts(int numberOfBatches)
        => Enumerable.Range(0, numberOfBatches)
            .Select(i => new BatchContextStub([new MessageStub()], i + 1 < numberOfBatches))
            .ToArray();

    private static IBatchProducer CreateProducer()
    {
        var producerSpy = Substitute.For<IBatchProducer>();
        producerSpy
            .ProduceAsync(default!, default!, default)
            .ReturnsForAnyArgs(args =>
                Task.FromResult(new BatchProduceResult { Ok = (IReadOnlyCollection<IMessage>)args[1] }));
        return producerSpy;
    }
}

public sealed class MessageStub : IMessage;

public sealed class BatchFetcherStub(BatchContextStub[] contexts) : IBatchFetcher
{
    private int _index = 0;

    public Task<IBatchContext> FetchAndHoldAsync(CancellationToken ct)
    {
        return _index >= contexts.Length
            ? Task.FromResult<IBatchContext>(EmptyBatchContext.Instance)
            : Task.FromResult<IBatchContext>(contexts[_index++]);
    }
}

public sealed class BatchContextStub(IReadOnlyCollection<IMessage> messages, bool hasNext) : IBatchContext
{
    public IReadOnlyCollection<IMessage> Messages => messages;
    public Task CompleteAsync(IReadOnlyCollection<IMessage> ok, CancellationToken ct) => Task.CompletedTask;
    public Task<bool> HasNextAsync(CancellationToken ct) => Task.FromResult(hasNext);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}