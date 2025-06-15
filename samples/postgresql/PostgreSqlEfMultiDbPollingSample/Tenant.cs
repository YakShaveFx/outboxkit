using System.Collections;
using System.Collections.Frozen;

namespace PostgreSqlEfMultiDbPollingSample;

public sealed record TenantList(IReadOnlySet<string> Tenants) : IReadOnlyCollection<string>
{
    public IEnumerator<string> GetEnumerator() => Tenants.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public int Count => Tenants.Count;
}

public sealed class ConnectionStringProvider(FrozenDictionary<string, string> connectionStrings)
{
    public string GetConnectionString(string tenant)
        => connectionStrings.TryGetValue(tenant, out var connectionString)
            ? connectionString
            : throw new InvalidOperationException($"No connection string for tenant '{tenant}'");
}

public interface ITenantProvider
{
    string Tenant { get; }
}

public sealed class TenantProvider : ITenantProvider
{
    private string? _tenant;

    public string Tenant => _tenant ?? throw new InvalidOperationException("Tenant not set");

    public void SetTenant(string tenant) => _tenant = tenant;
}

public class TenantMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, TenantProvider tenantProvider)
    {
        if (context.Request.Headers.TryGetValue("X-Tenant", out var tenantValues))
        {
            string? tenant = tenantValues;
            if (tenant is not null)
            {
                tenantProvider.SetTenant(tenant);
            }
        }

        await next(context);
    }
}