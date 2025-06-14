namespace YakShaveFx.OutboxKit.PostgreSql.CleanUp;

internal sealed record PostgreSqlCleanUpSettings
{
    public TimeSpan MaxAge { get; init; } = TimeSpan.FromDays(1);
}