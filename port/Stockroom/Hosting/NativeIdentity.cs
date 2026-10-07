using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
namespace Stockroom.Hosting;
internal static class NativeIdentity
{
    internal static void Configure(IServiceCollection services, HostSettings settings)
    {
        var authentication = services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o => {
            o.Cookie.Name = "__Host-Stockroom.Auth"; o.Cookie.Path = "/"; o.Cookie.HttpOnly = true;
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always; o.Cookie.SameSite = settings.IsDeployment ? SameSiteMode.Lax : SameSiteMode.Strict;
            o.LoginPath = "/account/login"; o.ExpireTimeSpan = TimeSpan.FromMinutes(20); o.SlidingExpiration = false;
            o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return c.Response.WriteAsync("Editor access is required."); };
            o.Events.OnRedirectToLogin = c => {
                if (c.Request.Path.Value.EndsWith("/SaveJson", StringComparison.OrdinalIgnoreCase)) c.Response.StatusCode = 401;
                else c.Response.Redirect(c.Request.PathBase + "/account/login");
                return Task.CompletedTask;
            };
        });
        if (!settings.IsDeployment) return;
        var s = settings.Deployment;
        authentication.AddOpenIdConnect(o => {
            o.Authority = s.Authority; o.ClientId = s.ClientId; o.ClientSecret = s.ClientSecret;
            o.RequireHttpsMetadata = true; o.ResponseType = OpenIdConnectResponseType.Code;
            o.ResponseMode = OpenIdConnectResponseMode.Query; o.UsePkce = true;
            o.MapInboundClaims = false; o.SaveTokens = false; o.GetClaimsFromUserInfoEndpoint = false;
            o.Scope.Clear(); o.Scope.Add("openid"); o.Scope.Add("profile");
            o.CallbackPath = "/signin-oidc"; o.RemoteAuthenticationTimeout = TimeSpan.FromMinutes(5);
            o.UseTokenLifetime = false; o.BackchannelTimeout = TimeSpan.FromSeconds(15);
            o.TokenValidationParameters.ValidIssuer = s.Issuer;
            o.TokenValidationParameters.IssuerValidator = (issuer, token, parameters) => issuer == s.Issuer ? issuer : throw new Microsoft.IdentityModel.Tokens.SecurityTokenInvalidIssuerException("Unexpected issuer.");
            o.TokenValidationParameters.ValidAudience = s.ClientId;
            o.TokenValidationParameters.ValidateIssuer = true; o.TokenValidationParameters.ValidateAudience = true;
            o.TokenValidationParameters.ValidateLifetime = true; o.TokenValidationParameters.RequireSignedTokens = true;
            o.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
            o.TokenValidationParameters.NameClaimType = s.NameClaim; o.TokenValidationParameters.RoleClaimType = s.RoleClaim;
            o.NonceCookie.SecurePolicy = o.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
            o.NonceCookie.HttpOnly = o.CorrelationCookie.HttpOnly = true;
            o.NonceCookie.SameSite = o.CorrelationCookie.SameSite = SameSiteMode.None;
            o.Events.OnTokenValidated = c => {
                var subjects = c.Principal.FindAll("sub").ToArray();
                if (subjects.Length != 1 || String.IsNullOrWhiteSpace(subjects[0].Value) || subjects[0].Value.Length > 256 || c.Principal.Claims.Count() > 64 || c.Principal.Claims.Any(x => x.Value.Length > 512))
                    c.Fail("Invalid identity claims.");
                return Task.CompletedTask;
            };
            o.Events.OnRemoteFailure = c => { c.HandleResponse(); c.Response.StatusCode = 400; return c.Response.WriteAsync("Identity sign-in failed. Start a new sign-in."); };
        });
    }
}
