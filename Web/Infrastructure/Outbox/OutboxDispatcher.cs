using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Web.Data;
using Web.MultiTenancy;
using Web.Realtime;

namespace Web.Infrastructure.Outbox;

public sealed class OutboxDispatcher(ITenantScopeFactory tenantScopeFactory, TenantRegistry registry, ILogger<OutboxDispatcher> logger, IHubContext<TenantHub> hub) : BackgroundService
{
	private readonly ITenantScopeFactory _tenantScopeFactory = tenantScopeFactory;
	private readonly TenantRegistry _registry = registry;
	private readonly ILogger<OutboxDispatcher> _logger = logger;
	private readonly IHubContext<TenantHub> _hub = hub;

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				foreach (var tenantKey in _registry.Tenants.Keys)
				{
					await using var scope = (await _tenantScopeFactory.CreateScopeForTenantAsync(tenantKey)).AsAsyncDisposable();
					var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

					var messages = await db.Set<OutboxMessage>()
						.Where(m => m.ProcessedOnUtc == null)
						.OrderBy(m => m.OccurredOnUtc)
						.Take(50)
						.ToListAsync(stoppingToken);

					foreach (var msg in messages)
					{
						try
						{
							await _hub.Clients.Group(tenantKey).SendAsync("tenantMessage", new { tenant = tenantKey, message = $"Outbox {msg.Type}: {msg.Payload}", at = DateTimeOffset.UtcNow }, stoppingToken);
							msg.ProcessedOnUtc = DateTimeOffset.UtcNow;
						}
						catch (Exception ex)
						{
							msg.Attempts++;
							msg.Error = ex.Message;
							_logger.LogError(ex, "Error dispatching outbox message {MessageId} for tenant {Tenant}", msg.Id, tenantKey);
						}
					}

					await db.SaveChangesAsync(stoppingToken);
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Outbox dispatcher loop error");
			}

			await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
		}
	}
}