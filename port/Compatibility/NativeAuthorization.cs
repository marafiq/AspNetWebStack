using System;
using System.Web;
using System.Web.Routing;
using Microsoft.AspNetCore.Authentication;

namespace AspNetWebStack.Native
{
    internal static class NativeAuthorization
    {
        internal static void CheckPrincipal(HttpContextBase context)
        {
            if (context is not INativeRoutingContext routing) return;
            var core = routing.CoreContext;
            var user = context.User;
            if (user.Identity?.IsAuthenticated != true) return;
            var result = core.Features.Get<IAuthenticateResultFeature>()?.AuthenticateResult;
            if (result?.Succeeded != true || !ReferenceEquals(result.Principal, core.User))
                throw new InvalidOperationException("Authenticated MVC requests require the trusted native authentication result for the current principal.");
        }
        internal static bool PreventCaching(HttpContextBase context)
        {
            if (context is not INativeRoutingContext routing) return false;
            var core = routing.CoreContext;
            if (core.Response.HasStarted) throw new InvalidOperationException("Authorization must precede the native response.");
            // Shared HTTP response caching is not installed. Child fragment
            // caching admits only anonymous, stateless invocations after filters.
            core.Response.Headers.CacheControl = "no-cache, no-store";
            core.Response.Headers.Pragma = "no-cache";
            return true;
        }
    }
}
