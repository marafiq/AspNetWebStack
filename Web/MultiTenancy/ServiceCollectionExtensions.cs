using Microsoft.Extensions.Options;

namespace Web.MultiTenancy;

public static class ServiceCollectionExtensions
{
	public static IServiceCollection AddMultiTenancy(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddSingleton(_ => TenantRegistry.FromConfiguration(configuration));
		services.AddScoped<TenantContext>();
		services.AddSingleton<TenantDatabaseMigrator>();
		services.Configure<MultiTenancyOptions>(configuration.GetSection("MultiTenancy"));
		return services;
	}

	public static IApplicationBuilder UseMultiTenancy(this IApplicationBuilder app)
	{
		return app.UseMiddleware<TenantMiddleware>();
	}
}