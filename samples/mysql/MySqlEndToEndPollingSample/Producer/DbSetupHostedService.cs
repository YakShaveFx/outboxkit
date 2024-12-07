using MySqlConnector;

namespace MySqlEndToEndPollingSample.Producer;

internal sealed class DbSetupHostedService(MySqlDataSource dataSource) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new MySqlCommand(
            // lang=mysql
            $"""
             create table if not exists outbox_messages
             (
                 id                    bigint auto_increment primary key,
                 type                  varchar(128) not null,
                 payload               longblob     not null,
                 created_at            datetime(6)  not null,
                 trace_context         longblob     null
             );
             """, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}