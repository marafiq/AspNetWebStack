using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Web.Data;
using Web.MultiTenancy;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Remove single static connection string usage and wire tenant-aware DbContext
builder.Services.AddMultiTenancy(builder.Configuration);

builder.Services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
{
	var tenantContext = serviceProvider.GetRequiredService<TenantContext>();
	var connection = tenantContext.Settings?.ConnectionString ?? builder.Configuration.GetConnectionString("DefaultConnection")!;
	options.UseSqlite(connection);
});

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = true)
	.AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddControllersWithViews();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
	app.UseMigrationsEndPoint();
}
else
{
	app.UseExceptionHandler("/Home/Error");
	// The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
	app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

// Tenant middleware as early as possible after routing and before auth
app.UseMultiTenancy();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
	name: "default",
	pattern: "{controller=Home}/{action=Index}/{id?}")
	.WithStaticAssets();

app.MapRazorPages()
   .WithStaticAssets();

app.Run();
