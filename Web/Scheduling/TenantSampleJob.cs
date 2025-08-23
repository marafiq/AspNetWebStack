using Microsoft.AspNetCore.SignalR;
using Quartz;
using Web.MultiTenancy;
using Web.Realtime;

namespace Web.Scheduling;

[DisallowConcurrentExecution]
public sealed class TenantSampleJob : IJob
{
	private readonly ITenantScopeFactory _tenantScopeFactory;
	private readonly IHubContext<TenantHub> _hubContext;

	public TenantSampleJob(ITenantScopeFactory tenantScopeFactory, IHubContext<TenantHub> hubContext)
	{
		_tenantScopeFactory = tenantScopeFactory;
		_hubContext = hubContext;
	}

	public async Task Execute(IJobExecutionContext context)
	{
		var tenantKey = context.MergedJobDataMap.GetString("tenantKey") ?? "default";
		await using var scope = (await _tenantScopeFactory.CreateScopeForTenantAsync(tenantKey)).AsAsyncDisposable();
		await _hubContext.Clients.Group(tenantKey).SendAsync("tenantMessage", new { tenant = tenantKey, message = $"Job ping at {DateTimeOffset.UtcNow:O}", at = DateTimeOffset.UtcNow });
	}
}