namespace MySqlEndToEndPollingSample.Consumer;

internal sealed class RabbitMqSettings
{
    public required string Host { get; init; } = "localhost";
    public required int Port { get; init; } = 5672;
    public required string Exchange { get; init; }
    public required string Queue { get; init; }
}