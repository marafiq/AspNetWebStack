using System;
using System.Collections;
using System.Security.Principal;
using System.Web;
using System.Web.Routing;

namespace AspNetWebStack.Native
{
    // Construct after asynchronous JSON preparation. Public wrappers retain their
    // synchronous owner; the reusable application supplies its sequential lifetime.
    public sealed class NativeJsonContext : HttpContextBase, INativeRoutingContext, IDisposable
    {
        private readonly NativeQueryContext _inner;
        public NativeJsonContext(Microsoft.AspNetCore.Http.HttpContext core, HttpResponseBase response,
            NativeJsonInput input, Action<string, string> validateQueryValue, int maximumQueryLength = 8192, int maximumQueryValues = 256)
            : this(core, response, input, validateQueryValue, maximumQueryLength, maximumQueryValues, null) { }
        internal NativeJsonContext(Microsoft.AspNetCore.Http.HttpContext core, HttpResponseBase response,
            NativeJsonInput input, Action<string, string> validateQueryValue, int maximumQueryLength, int maximumQueryValues, NativeRequestLifetime lifetime)
        {
            ArgumentNullException.ThrowIfNull(input);
            _inner = new NativeQueryContext(core, response, validateQueryValue, maximumQueryLength, maximumQueryValues, null, null, input, lifetime);
        }
        Microsoft.AspNetCore.Http.HttpContext INativeRoutingContext.CoreContext => ((INativeRoutingContext)_inner).CoreContext;
        internal NativeRequestLifetime NativeLifetime => _inner.NativeLifetime;
        public override HttpRequestBase Request => _inner.Request;
        public override HttpResponseBase Response => _inner.Response;
        public override IDictionary Items => _inner.Items;
        public override IPrincipal User { get => _inner.User; set => throw new PlatformNotSupportedException("The native host owns the request principal."); }
        public override HttpSessionStateBase Session => _inner.Session;
        public override bool IsCustomErrorEnabled => _inner.IsCustomErrorEnabled;
        public void Dispose() => _inner.Dispose();
    }
}
