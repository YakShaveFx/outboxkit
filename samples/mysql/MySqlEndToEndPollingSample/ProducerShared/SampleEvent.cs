namespace MySqlEndToEndPollingSample.ProducerShared;

public sealed class SampleEvent
{
    public required Guid Id { get; init; }
    public required string Verb { get; init; }
}