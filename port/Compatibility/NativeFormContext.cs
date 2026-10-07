using System;
using System.Collections;
using System.Security.Principal;
using System.Web;
using System.Web.Routing;

namespace AspNetWebStack.Native
{
    // Construct after awaiting NativeFormInput.ReadAsync. Public construction keeps
    // synchronous ownership; the native application opts into awaited ownership.
    public sealed class NativeFormContext : HttpContextBase, INativeRoutingContext, IDisposable
    {
        private readonly NativeQueryContext _inner;
        public NativeFormContext(Microsoft.AspNetCore.Http.HttpContext core, HttpResponseBase response,
            NativeFormInput input, Action<string, string> validateQueryValue, Action<string, string> validateFormValue,
            int maximumQueryLength = 8192, int maximumQueryValues = 256)
            : this(core, response, input, validateQueryValue, validateFormValue, maximumQueryLength, maximumQueryValues, null) { }
        internal NativeFormContext(Microsoft.AspNetCore.Http.HttpContext core, HttpResponseBase response,
            NativeFormInput input, Action<string, string> validateQueryValue, Action<string, string> validateFormValue,
            int maximumQueryLength, int maximumQueryValues, NativeRequestLifetime lifetime)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(validateFormValue);
            _inner = new NativeQueryContext(core, response, validateQueryValue, maximumQueryLength, maximumQueryValues, input, validateFormValue, lifetime: lifetime);
        }
        Microsoft.AspNetCore.Http.HttpContext INativeRoutingContext.CoreContext => ((INativeRoutingContext)_inner).CoreContext;
        public override HttpRequestBase Request => _inner.Request;
        public override HttpResponseBase Response => _inner.Response;
        public override IDictionary Items => _inner.Items;
        public override IPrincipal User { get => _inner.User; set => throw new PlatformNotSupportedException("The native host owns the request principal."); }
        public override HttpSessionStateBase Session => _inner.Session;
        public override bool IsCustomErrorEnabled => _inner.IsCustomErrorEnabled;
        internal NativeRequestLifetime NativeLifetime => _inner.NativeLifetime;
        public void Dispose() => _inner.Dispose();
    }

    internal interface INativeFormRequest : System.Web.Mvc.IUnvalidatedRequestValues
    {
        bool HasFormInput { get; }
        System.Collections.Specialized.NameValueCollection FormCollectionValues(bool validated);
        System.Collections.Specialized.NameValueCollection ValidatedForm { get; }
    }
    internal static class NativeFormValues
    {
        // Only the admitted native boundary can supply the whole-form snapshot.
        // In particular, an arbitrary/unprepared request must not become an empty form.
        internal static System.Collections.Specialized.NameValueCollection Snapshot(HttpRequestBase request, bool validated) => request is INativeFormRequest native
            ? native.FormCollectionValues(validated) : throw new PlatformNotSupportedException("FormCollection requires admitted native input.");
        internal static bool Available(HttpRequestBase request) => request is INativeFormRequest form && form.HasFormInput;
        internal static System.Web.Mvc.IUnvalidatedRequestValues Resolve(HttpRequestBase request) => Available(request)
            ? (INativeFormRequest)request : throw new PlatformNotSupportedException("Form providers require prepared native form input.");
        internal static System.Collections.Specialized.NameValueCollection Validated(HttpRequestBase request) => Available(request)
            ? ((INativeFormRequest)request).ValidatedForm : throw new PlatformNotSupportedException("Form providers require prepared native form input.");
    }
}
