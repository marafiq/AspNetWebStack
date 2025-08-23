using Microsoft.AspNetCore.Mvc;
using Web.MultiTenancy;
using Web.Infrastructure.Outbox;
using Web.Data;

namespace Web.Controllers;

[Route("components")]
public sealed class ComponentsController : Controller
{
	private readonly TenantContext _tenantContext;
	private readonly ApplicationDbContext _db;
	public ComponentsController(TenantContext tenantContext, ApplicationDbContext db)
	{
		_tenantContext = tenantContext;
		_db = db;
	}

	[HttpGet("tenant-banner")]
	public IActionResult TenantBanner()
	{
		return PartialView("~/Views/Components/TenantBanner.cshtml", _tenantContext);
	}

	[HttpPost("counter/increment")]
	public IActionResult IncrementCounter()
	{
		var key = $"counter::{_tenantContext.Tenant?.Key ?? "default"}";
		var current = HttpContext.Session.GetInt32(key) ?? 0;
		current++;
		HttpContext.Session.SetInt32(key, current);
		Response.Headers["HX-Trigger"] = $"{{\"CounterChanged\":{{\"tenant\":\"{_tenantContext.Tenant?.Key}\",\"value\":{current}}}}}";
		return PartialView("~/Views/Components/Counter.cshtml", current);
	}

	[HttpGet("counter")]
	public IActionResult Counter()
	{
		var key = $"counter::{_tenantContext.Tenant?.Key ?? "default"}";
		var current = HttpContext.Session.GetInt32(key) ?? 0;
		return PartialView("~/Views/Components/Counter.cshtml", current);
	}

	[HttpPost("outbox/publish")]
	public async Task<IActionResult> PublishOutbox([FromForm] string? message)
	{
		var tenantKey = _tenantContext.Tenant?.Key ?? "default";
		var evt = new OutboxMessage
		{
			Id = Guid.NewGuid(),
			TenantKey = tenantKey,
			Type = "Demo",
			Payload = string.IsNullOrWhiteSpace(message) ? "Ping" : message!,
			OccurredOnUtc = DateTimeOffset.UtcNow
		};
		_db.OutboxMessages.Add(evt);
		await _db.SaveChangesAsync();
		Response.Headers["HX-Trigger"] = $"{{\"OutboxEnqueued\":{{\"tenant\":\"{tenantKey}\",\"id\":\"{evt.Id}\"}}}}";
		return NoContent();
	}
}