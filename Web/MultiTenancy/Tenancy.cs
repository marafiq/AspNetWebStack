namespace Web.MultiTenancy;

public sealed class TenantDefinition
{
	public required string Key { get; init; }
	public string? Name { get; init; }
	public required string Group { get; init; }
	public string? ConnectionStringOverride { get; init; }
}

public sealed class TenantGroupConfig
{
	public required string Name { get; init; }
	public string? SqlitePathTemplate { get; init; } = "Data/tenants/{tenant}.db";
	public BlobConfig Blob { get; init; } = new();
	public CloudinaryConfig Cloudinary { get; init; } = new();
}

public sealed class BlobConfig
{
	public string? ConnectionString { get; init; }
	public string? Container { get; init; }
}

public sealed class CloudinaryConfig
{
	public string? CloudName { get; init; }
	public string? ApiKey { get; init; }
	public string? ApiSecret { get; init; }
}

public sealed class TenantAppSettings
{
	public required string ConnectionString { get; init; }
	public TenantGroupConfig Group { get; init; } = new();
}

public sealed class TenantContext
{
	public TenantDefinition? Tenant { get; internal set; }
	public TenantAppSettings? Settings { get; internal set; }

	public string GetConnectionStringOrThrow()
	{
		if (Settings?.ConnectionString is { Length: > 0 } cs) return cs;
		throw new InvalidOperationException("Tenant connection string not resolved for this request.");
	}
}

public sealed class TenantRegistry
{
	public required IReadOnlyDictionary<string, TenantDefinition> Tenants { get; init; }
	public required IReadOnlyDictionary<string, TenantGroupConfig> Groups { get; init; }
	public required string DefaultTenantKey { get; init; }

	public static TenantRegistry FromConfiguration(IConfiguration configuration)
	{
		var tenants = configuration.GetSection("Tenants").Get<List<TenantDefinition>>() ?? [];
		var groupsList = configuration.GetSection("TenantGroups").Get<List<TenantGroupConfig>>() ?? [];
		var groups = groupsList.ToDictionary(g => g.Name, StringComparer.OrdinalIgnoreCase);
		var tenantsDict = tenants.ToDictionary(t => t.Key, StringComparer.OrdinalIgnoreCase);
		var defaultKey = configuration.GetValue<string>("DefaultTenant") ?? tenants.FirstOrDefault()?.Key ?? "alpha";
		return new TenantRegistry
		{
			Tenants = tenantsDict,
			Groups = groups,
			DefaultTenantKey = defaultKey
		};
	}

	public bool TryResolve(string tenantKey, out TenantDefinition tenant, out TenantGroupConfig group)
	{
		if (Tenants.TryGetValue(tenantKey, out tenant!))
		{
			if (Groups.TryGetValue(tenant.Group, out group!)) return true;
		}
		tenant = null!;
		group = null!;
		return false;
	}

	public (TenantDefinition tenant, TenantGroupConfig group) ResolveOrDefault(string? tenantKey)
	{
		var key = string.IsNullOrWhiteSpace(tenantKey) ? DefaultTenantKey : tenantKey!;
		if (!TryResolve(key, out var tenant, out var group))
		{
			if (!TryResolve(DefaultTenantKey, out tenant, out group))
			{
				throw new InvalidOperationException("No valid tenant configuration found.");
			}
		}
		return (tenant, group);
	}
}