using FluentAssertions;
using YakShaveFx.OutboxKit.Core.Polling;

namespace YakShaveFx.OutboxKit.Core.Tests.Polling;

public class ListenerTests
{
    [Fact]
    public void WhenListeningForMessagesThenTheTaskRemainsInProgress()
    {
        var sut = new Listener();
        
        var listenerTask = sut.WaitForMessagesAsync(CancellationToken.None);

        listenerTask.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public void WhenListeningForMessagesAndItIsTriggeredTriggeredThenTheTaskCompletes()
    {
        var sut = new Listener();

        var listenerTask = sut.WaitForMessagesAsync(CancellationToken.None);
        sut.OnNewMessages();

        listenerTask.IsCompleted.Should().BeTrue();
    }
    
    [Fact]
    public void WhenTriggeringBeforeListeningForMessagesThenTheTaskCompletes()
    {
        var sut = new Listener();

        sut.OnNewMessages();
        var listenerTask = sut.WaitForMessagesAsync(CancellationToken.None);

        listenerTask.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public void WhenListeningForMessagesWithAnyKeyThenTheTaskRemainsInProgress()
    {
        var sut = new Listener();

        var listenerTask = sut.WaitForMessagesAsync(new ("sample-provider", "some-key"), CancellationToken.None);

        listenerTask.IsCompleted.Should().BeFalse();
    }

    [Theory]
    [InlineData("some-key", "some-key")]
    [InlineData("some-key", "some-other-key")]
    public void WhenListeningForMessagesWithAnyKeyAndItIsTriggeredThenTheTaskCompletes(
        string listenKey, string triggerKey)
    {
        var sut = new Listener();

        var listenerTask = sut.WaitForMessagesAsync(new ("sample-provider", listenKey), CancellationToken.None);
        sut.OnNewMessages(new ("sample-provider", triggerKey));

        listenerTask.IsCompleted.Should().BeTrue();
    }
}