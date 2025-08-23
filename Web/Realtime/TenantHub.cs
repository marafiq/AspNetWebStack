using Microsoft.AspNetCore.SignalR;
using Web.MultiTenancy;

namespace Web.Realtime;

public sealed class TenantHub : Hub
{
	private readonly TenantContext _tenantContext;

	public TenantHub(TenantContext tenantContext)
	{
		_tenantContext = tenantContext;
	}

	public override async Task OnConnectedAsync()
	{
		var tenantKey = _tenantContext.Tenant?.Key ?? "default";
		await Groups.AddToGroupAsync(Context.ConnectionId, tenantKey);
		await base.OnConnectedAsync();
	}

	public Task SendTenantMessage(string message)
	{
		var tenantKey = _tenantContext.Tenant?.Key ?? "default";
		return Clients.Group(tenantKey).SendAsync("tenantMessage", new { tenant = tenantKey, message, at = DateTimeOffset.UtcNow });
	}
}