namespace YakShaveFx.OutboxKit.PostgreSql;

internal static class Extensions
{
    public static string Quote(this string value)
        => value switch
        {
            _ when value.StartsWith('"') && value.EndsWith('"') => value,
            _ => $"\"{value}\""
        };
}