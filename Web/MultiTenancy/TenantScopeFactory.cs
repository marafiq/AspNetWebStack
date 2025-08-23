namespace Web.MultiTenancy;

public interface ITenantScopeFactory
{
	Task<IServiceScope> CreateScopeForTenantAsync(string tenantKey);
}

public sealed class TenantScopeFactory(IServiceProvider rootProvider) : ITenantScopeFactory
{
	private readonly IServiceProvider _rootProvider = rootProvider;

	public Task<IServiceScope> CreateScopeForTenantAsync(string tenantKey)
	{
		var scope = _rootProvider.CreateScope();
		var registry = scope.ServiceProvider.GetRequiredService<TenantRegistry>();
		var (tenant, group) = registry.ResolveOrDefault(tenantKey);
		var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();

		var template = group.SqlitePathTemplate ?? "Data/tenants/{tenant}.db";
		var dbPath = template.Replace("{tenant}", tenant.Key, StringComparison.OrdinalIgnoreCase);
		var resolvedConnection = tenant.ConnectionStringOverride ?? $"Data Source={dbPath};Cache=Shared";

		tenantContext.Tenant = tenant;
		tenantContext.Settings = new TenantAppSettings
		{
			ConnectionString = resolvedConnection,
			Group = group
		};

		return Task.FromResult(scope);
	}
}