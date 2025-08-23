using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Web.MultiTenancy;

public sealed class TenantMiddleware(RequestDelegate next)
{
	private static readonly Regex HostRegex = new("^(?<sub>[^.]+)\\.", RegexOptions.IgnoreCase | RegexOptions.Compiled);
	private readonly RequestDelegate _next = next;

	public async Task InvokeAsync(HttpContext httpContext, TenantRegistry registry, TenantContext tenantContext, IOptionsMonitor<MultiTenancyOptions> options)
	{
		var host = httpContext.Request.Host.Host;
		string? subdomain = null;
		var match = HostRegex.Match(host);
		if (match.Success)
		{
			subdomain = match.Groups["sub"].Value;
		}

		var (tenant, group) = registry.ResolveOrDefault(subdomain);

		var resolvedConnection = tenant.ConnectionStringOverride;
		if (string.IsNullOrWhiteSpace(resolvedConnection))
		{
			var template = group.SqlitePathTemplate ?? options.CurrentValue.DefaultSqlitePathTemplate;
			var dbPath = template.Replace("{tenant}", tenant.Key, StringComparison.OrdinalIgnoreCase);
			resolvedConnection = $"Data Source={dbPath};Cache=Shared";
		}

		tenantContext.Tenant = tenant;
		tenantContext.Settings = new TenantAppSettings
		{
			ConnectionString = resolvedConnection!,
			Group = group
		};

		httpContext.Items[nameof(TenantContext)] = tenantContext;

		// Ensure tenant database is migrated
		var migrator = httpContext.RequestServices.GetRequiredService<TenantDatabaseMigrator>();
		await migrator.EnsureMigratedAsync(httpContext.RequestServices);

		await _next(httpContext);
	}
}

public sealed class MultiTenancyOptions
{
	public string DefaultSqlitePathTemplate { get; init; } = "Data/tenants/{tenant}.db";
}