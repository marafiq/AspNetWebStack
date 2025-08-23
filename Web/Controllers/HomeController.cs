using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Web.Models;
using Web.MultiTenancy;

namespace Web.Controllers;

public class HomeController : Controller
{
	private readonly TenantContext _tenantContext;

	public HomeController(TenantContext tenantContext)
	{
		_tenantContext = tenantContext;
	}

	public IActionResult Index()
	{
		ViewData["TenantKey"] = _tenantContext.Tenant?.Key ?? "unknown";
		ViewData["TenantName"] = _tenantContext.Tenant?.Name ?? "Unknown";
		ViewData["TenantGroup"] = _tenantContext.Tenant?.Group ?? "Unknown";
		ViewData["ConnString"] = _tenantContext.Settings?.ConnectionString ?? "";
		return View();
	}

	public IActionResult Privacy()
	{
		return View();
	}

	[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
	public IActionResult Error()
	{
		return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
	}
}
