namespace YakShaveFx.OutboxKit.MySql.CleanUp;

internal sealed record MySqlCleanUpSettings
{
    public TimeSpan MaxAge { get; init; } = TimeSpan.FromDays(1);
}