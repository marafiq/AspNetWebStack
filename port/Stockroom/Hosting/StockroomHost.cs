using AspNetWebStack.Native;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;
using Stockroom;
using Stockroom.Hosting;

namespace Stockroom.Hosting;
public static class StockroomHost
{
public static async Task RunAsync(string[] args, IHostInfrastructure infrastructure = null)
{
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ApplicationName = typeof(StockController).Assembly.GetName().Name });
builder.Host.UseDefaultServiceProvider(o => { o.ValidateScopes = true; o.ValidateOnBuild = true; });
var settings = HostSettings.Read(builder.Configuration);
string pathBase = settings.PathBase;
// Validate local accounts before constructing transport or warming any protection service.
if (!settings.IsDeployment) builder.Services.AddSingleton(new ConfiguredAccounts(builder.Configuration));
using var transport = settings.IsDeployment ? (infrastructure ?? new DeploymentInfrastructure()).Configure(builder, settings) : LocalEvaluation.Configure(builder);
if (settings.IsDeployment && settings.Deployment.TrustedProxy) builder.Services.Configure<ForwardedHeadersOptions>(o => {
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1; o.KnownProxies.Clear(); o.KnownIPNetworks.Clear();
    foreach (var proxy in settings.Deployment.KnownProxies) o.KnownProxies.Add(proxy);
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddNativeMvcRouting();
NativeIdentity.Configure(builder.Services, settings);
// Public MVC actions remain public; original [Authorize] decides per action.
builder.Services.AddAuthorization(o => o.AddPolicy("MvcApplication", p => p.RequireAssertion(_ => true)));
builder.Services.AddAntiforgery(o => {
    o.FormFieldName = "__RequestVerificationToken"; o.HeaderName = "X-Stockroom-CSRF";
    o.Cookie.Name = "__Host-Stockroom.Csrf"; o.Cookie.Path = "/"; o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always; o.Cookie.SameSite = SameSiteMode.Strict;
});
builder.Services.Configure<FormOptions>(o => { o.ValueCountLimit = 32; o.KeyLengthLimit = 128; o.ValueLengthLimit = 4096; });
builder.Services.AddSingleton<IStockStore>(settings.IsDeployment ? new SqliteStockStore(settings.Deployment.DatabasePath) : new MemoryStockStore());
builder.Services.AddScoped<IStockEditor, StockEditor>();
builder.Services.AddScoped<INativeMvcTransaction, StockTransaction>();
var app = builder.Build();
if (!settings.IsDeployment) LocalEvaluation.Verify(app.Services);
if (settings.IsDeployment && settings.Deployment.TrustedProxy) app.UseForwardedHeaders();
System.Web.Mvc.DependencyResolver.SetResolver(new NativeDependencyResolver(app.Services));
if (pathBase != "/") {
    app.UsePathBase(pathBase);
    app.Use(async (c, next) => { if (c.Request.PathBase != pathBase) { c.Response.StatusCode = 404; await c.Response.WriteAsync("Not found."); return; } await next(c); });
}
app.Use(async (c, next) => {
    if (!c.Request.IsHttps || (settings.IsDeployment && !String.Equals(c.Request.Host.Value, settings.Deployment.PublicOrigin.Authority, StringComparison.OrdinalIgnoreCase))) { c.Response.StatusCode = 400; return; }
    c.Response.Headers.CacheControl = "no-store";
    c.Response.Headers["X-Content-Type-Options"] = "nosniff";
    await next(c);
});
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();
AccountEndpoints.Map(app, settings);
app.MapGet("/health", () => Results.Text("ready"));
using var mvc = new NativeMvcApplication(app.Services, typeof(StockController).Assembly,
    "MvcApplication", "__Host-Stockroom.TempData", ValidateInput, pathBase);
mvc.Map(app, routes => System.Web.Mvc.RouteCollectionExtensions.MapRoute(routes, "stockroom", "{controller}/{action}", new { controller = "Stock", action = "Index" }));
app.Logger.LogInformation("Application identity {Identity}", System.Text.Json.JsonSerializer.Serialize(new {
    runtime = Environment.Version.ToString(),
    assemblies = new[] { typeof(StockController).Assembly, typeof(System.Web.Mvc.Controller).Assembly, typeof(System.Web.Razor.RazorTemplateEngine).Assembly, typeof(System.Web.HttpContextBase).Assembly }.Select(a => new {
        name = a.GetName().Name, path = a.Location, sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(a.Location))).ToLowerInvariant()
    })
}));
await app.RunAsync();
}

static void ValidateInput(string key, string value) {
    foreach (var text in new[] { key, value })
        if (text != null && (text.Contains('<') || text.Contains('>') || text.Any(Char.IsControl)))
            throw new System.Web.HttpException(400, "Stockroom accepts plain single-line input.");
}
}
