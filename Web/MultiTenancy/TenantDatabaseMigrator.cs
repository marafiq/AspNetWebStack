using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Web.Data;

namespace Web.MultiTenancy;

public sealed class TenantDatabaseMigrator
{
	private readonly ConcurrentDictionary<string, Lazy<Task>> _migrations = new(StringComparer.OrdinalIgnoreCase);

	public Task EnsureMigratedAsync(IServiceProvider services)
	{
		var tenantContext = services.GetRequiredService<TenantContext>();
		var tenantKey = tenantContext.Tenant?.Key ?? "default";
		return _migrations.GetOrAdd(tenantKey, _ => new Lazy<Task>(() => MigrateInternalAsync(services), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
	}

	private static async Task MigrateInternalAsync(IServiceProvider services)
	{
		var tenantContext = services.GetRequiredService<TenantContext>();
		var connection = tenantContext.GetConnectionStringOrThrow();

		// Ensure directory exists for SQLite file
		var builder = new SqliteConnectionStringBuilder(connection);
		var dataSource = builder.DataSource;
		if (!string.IsNullOrWhiteSpace(dataSource))
		{
			var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
			if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
			{
				Directory.CreateDirectory(directory);
			}
		}

		using var scope = services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
		await db.Database.MigrateAsync();
	}
}