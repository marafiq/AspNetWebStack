using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace AspNetWebStack.Native
{
    // The host establishes a principal before synchronous MVC ownership starts.
    // Compare both reference and claims: ClaimsPrincipal/ClaimsIdentity are mutable.
    internal sealed class NativePrincipalSnapshot
    {
        private readonly ClaimsPrincipal _principal;
        private readonly string _claims;
        private readonly IAuthenticateResultFeature _authentication;
        private readonly AuthenticateResult _result;
        internal NativePrincipalSnapshot(HttpContext context)
        {
            _principal = context.User;
            _claims = Describe(_principal);
            _authentication = context.Features.Get<IAuthenticateResultFeature>();
            _result = _authentication?.AuthenticateResult;
        }
        internal void Check(HttpContext context)
        {
            if (!ReferenceEquals(_principal, context.User) || _claims != Describe(context.User) ||
                !ReferenceEquals(_authentication, context.Features.Get<IAuthenticateResultFeature>()) ||
                !ReferenceEquals(_result, _authentication?.AuthenticateResult))
                throw new InvalidOperationException("The native principal and authentication result must remain unchanged during MVC request ownership.");
        }
        private static string Describe(ClaimsPrincipal principal) => JsonSerializer.Serialize(principal.Identities.Select(identity => new
        {
            identity.AuthenticationType, identity.IsAuthenticated, identity.Name, identity.NameClaimType, identity.RoleClaimType,
            Claims = identity.Claims.Select(claim => new { claim.Type, claim.Value, claim.ValueType, claim.Issuer, claim.OriginalIssuer,
                Properties = claim.Properties.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray() })
        }));
    }
}
