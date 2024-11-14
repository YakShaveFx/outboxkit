using FluentAssertions;
using YakShaveFx.OutboxKit.Core.Polling;

namespace YakShaveFx.OutboxKit.Core.Tests.Polling;

public class KeyedListenerTests
{
    private static readonly OutboxKey SomeKey = new("sample-provider", "some-key");
    private static readonly OutboxKey SomeOtherKey = new("sample-provider", "some-other-key");
    private static readonly OutboxKey NonExistentKey = new("sample-provider", "non-existent-key");
    private static readonly IEnumerable<OutboxKey> ValidKeys = [SomeKey, SomeOtherKey];

    [Fact]
    public void WhenListeningForMessagesWithAnyKeyThenTheTaskRemainsInProgress()
    {
        var sut = new KeyedListener(ValidKeys);

        var listenerTask = sut.WaitForMessagesAsync(SomeKey, CancellationToken.None);

        listenerTask.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public void WhenListeningForMessagesWithAKeyAndItIsTriggeredThenTheTaskCompletes()
    {
        var sut = new KeyedListener(ValidKeys);

        var listenerTask = sut.WaitForMessagesAsync(SomeKey, CancellationToken.None);
        sut.OnNewMessages(SomeKey);

        listenerTask.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public void WhenTriggeringBeforeListeningForMessagesWithAKeyThenTheTaskCompletes()
    {
        var sut = new KeyedListener(ValidKeys);

        sut.OnNewMessages(SomeKey);
        var listenerTask = sut.WaitForMessagesAsync(SomeKey, CancellationToken.None);

        listenerTask.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public void WhenListeningForMessagesWithAKeyAndAnotherIsTriggeredThenTheTaskRemainsInProgress()
    {
        var sut = new KeyedListener(ValidKeys);

        var listenerTask = sut.WaitForMessagesAsync(SomeKey, CancellationToken.None);
        sut.OnNewMessages(SomeOtherKey);

        listenerTask.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public void WhenListeningForMessagesWithAnInvalidKeyThenAnExceptionIsRaised()
    {
        var sut = new KeyedListener(ValidKeys);

        var act = () => sut.WaitForMessagesAsync(NonExistentKey, CancellationToken.None);

        act
            .Should()
            .ThrowAsync<ArgumentException>()
            .WithMessage($"Key {NonExistentKey} not found to wait for outbox messages*");
    }

    [Fact]
    public void WhenTriggeringMessagesWithAnInvalidKeyThenAnExceptionIsRaised()
    {
        var sut = new KeyedListener(ValidKeys);

        var act = () => sut.OnNewMessages(NonExistentKey);

        act
            .Should()
            .Throw<ArgumentException>()
            .WithMessage($"Key {NonExistentKey} not found to trigger outbox message production*");
    }
}