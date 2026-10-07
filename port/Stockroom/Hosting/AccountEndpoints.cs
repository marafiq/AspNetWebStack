using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
namespace Stockroom.Hosting;

internal sealed class ConfiguredAccounts
{
    private readonly PasswordHasher<string> _hasher = new();
    private readonly Dictionary<string, (string Hash, string Role)> _accounts = new(StringComparer.Ordinal);
    internal ConfiguredAccounts(IConfiguration config)
    {
        foreach (var entry in config.GetSection("Accounts").GetChildren()) {
            string name = entry["Name"], password = entry["Password"], role = entry["Role"];
            if (_accounts.Count == 10 || String.IsNullOrWhiteSpace(name) || name.Length > 64 || password == null || password.Length < 12 || password.Length > 256 || (role != "Editor" && role != "Reader"))
                throw new ArgumentException("Configure 1–10 unique Accounts entries with Name, a 12–256 character Password, and Editor or Reader Role. No built-in credentials.");
            _accounts.Add(name, (_hasher.HashPassword(name, password), role));
        }
        if (_accounts.Count == 0) throw new ArgumentException("Set Accounts__0__Name, Accounts__0__Password and Accounts__0__Role externally. See README.md.");
    }
    internal ClaimsPrincipal Authenticate(string name, string password)
    {
        if (name == null || password == null || password.Length > 256 || !_accounts.TryGetValue(name, out var account) ||
            _hasher.VerifyHashedPassword(name, account.Hash, password) == PasswordVerificationResult.Failed) return null;
        return new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, name), new Claim(ClaimTypes.Name, name), new Claim(ClaimTypes.Role, account.Role) }, CookieAuthenticationDefaults.AuthenticationScheme));
    }
}
internal static class AccountEndpoints
{
    internal static void Map(WebApplication app, HostSettings settings)
    {
        if (!settings.IsDeployment) {
        app.MapGet("/account/login", (HttpContext c, IAntiforgery anti) => LoginPage(c, anti, null));
        app.MapPost("/account/login", async (HttpContext c, IAntiforgery anti, ConfiguredAccounts accounts) => {
            try { await anti.ValidateRequestAsync(c); }
            catch (AntiforgeryValidationException) { return Results.BadRequest("Invalid request token."); }
            var form = await c.Request.ReadFormAsync(c.RequestAborted);
            var principal = accounts.Authenticate(form["name"], form["password"]);
            if (principal == null) { c.Response.StatusCode = 401; return LoginPage(c, anti, "The account or password is incorrect."); }
            await c.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties { IsPersistent = false });
            return Results.LocalRedirect(c.Request.PathBase + "/Stock");
        });
        } else {
            app.MapGet("/account/login", (HttpContext c) => Results.Challenge(new AuthenticationProperties { RedirectUri = c.Request.PathBase + "/Stock" }, new[] { Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectDefaults.AuthenticationScheme }));
            app.MapPost("/account/login", () => Results.NotFound());
        }
        app.MapPost("/account/logout", async (HttpContext c, IAntiforgery anti) => {
            try { await anti.ValidateRequestAsync(c); }
            catch (AntiforgeryValidationException) { return Results.BadRequest("Invalid request token."); }
            await c.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.LocalRedirect(c.Request.PathBase + "/Stock");
        });
    }
    private static IResult LoginPage(HttpContext c, IAntiforgery anti, string error)
    {
        var token = anti.GetAndStoreTokens(c); string E(string s) => HtmlEncoder.Default.Encode(s ?? "");
        return Results.Content($"<!doctype html><html><head><meta charset='utf-8'><title>Sign in · Stockroom</title><link rel='stylesheet' href='{E(c.Request.PathBase + "/site.css")}'></head><body><main><a href='{E(c.Request.PathBase + "/Stock")}'>Stockroom</a><h1>Sign in</h1><p role='alert'>{E(error)}</p><form method='post' action='{E(c.Request.PathBase + "/account/login")}'><input type='hidden' name='{E(token.FormFieldName)}' value='{E(token.RequestToken)}'><label>Account<input name='name' autocomplete='username' required maxlength='64'></label><label>Password<input name='password' type='password' autocomplete='current-password' required maxlength='256'></label><button>Sign in</button></form></main></body></html>", "text/html", statusCode: error == null ? 200 : 401);
    }
}
