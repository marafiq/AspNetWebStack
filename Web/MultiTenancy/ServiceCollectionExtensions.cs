using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Quartz;
using Web.Infrastructure.Outbox;
using Web.Realtime;
using Web.Scheduling;

namespace Web.MultiTenancy;

public static class ServiceCollectionExtensions
{
	public static IServiceCollection AddMultiTenancy(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddSingleton(_ => TenantRegistry.FromConfiguration(configuration));
		services.AddScoped<TenantContext>();
		services.AddSingleton<TenantDatabaseMigrator>();
		services.AddSingleton<ITenantScopeFactory, TenantScopeFactory>();
		services.Configure<MultiTenancyOptions>(configuration.GetSection("MultiTenancy"));

		// SignalR
		services.AddSignalR();

		// Quartz
		services.AddQuartz(q =>
		{
			q.UseMicrosoftDependencyInjectionJobFactory();
		});
		services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);
		services.AddHostedService<QuartzSchedulerHostedService>();

		// OpenTelemetry (simple console exporter)
		services.AddOpenTelemetry()
			.ConfigureResource(r => r.AddService("Web.MultiTenantSample"))
			.WithTracing(t =>
			{
				t.AddAspNetCoreInstrumentation();
				t.AddHttpClientInstrumentation();
				t.AddConsoleExporter();
			})
			.WithMetrics(m =>
			{
				m.AddAspNetCoreInstrumentation();
				m.AddHttpClientInstrumentation();
				m.AddRuntimeInstrumentation();
				m.AddProcessInstrumentation();
				m.AddConsoleExporter();
			});

		// Outbox dispatcher
		services.AddHostedService<OutboxDispatcher>();

		return services;
	}

	public static IApplicationBuilder UseMultiTenancy(this IApplicationBuilder app)
	{
		return app.UseMiddleware<TenantMiddleware>();
	}
}