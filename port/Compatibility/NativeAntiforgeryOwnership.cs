using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Web.Mvc;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace AspNetWebStack.Native
{
    // Core's opaque per-request cache is detected, never inspected or cleared.
    // If its implementation key changes, type resolution fails closed. This
    // boundary is verified against the pinned ASP.NET Core 10.0.1 implementation.
    internal static class NativeAntiforgeryOwnership
    {
        private static readonly Type CacheFeature = typeof(IAntiforgery).Assembly.GetType(
            "Microsoft.AspNetCore.Antiforgery.IAntiforgeryFeature", throwOnError: true);
        private static readonly ConditionalWeakTable<HttpContext, State> States = new();

        internal static T Run<T>(HttpContext context, IAntiforgery service, Func<T> operation, string headerName = null)
        {
            var state = States.GetValue(context, c => new State(c, service, headerName));
            lock (state)
            {
                bool entered = false;
                try
                {
                    // An entry rejection is terminal too: a provider must not
                    // catch a nested rejection and restore the outer operation.
                    state.Check(context, service, headerName);
                    state.Active = true;
                    entered = true;
                    T result = operation();
                    if (state.Cache != null && !ReferenceEquals(state.Cache, context.Features[CacheFeature]))
                        throw new HttpAntiForgeryException("The owned native antiforgery cache changed during validation.");
                    state.Cache = context.Features[CacheFeature];
                    state.Active = false;
                    state.Check(context, service, headerName);
                    return result;
                }
                catch { state.Failed = true; throw; }
                finally { if (entered) state.Active = false; }
            }
        }
        private sealed class State
        {
            private readonly IAntiforgery _service;
            private readonly NativePrincipalSnapshot _principal;
            private readonly string[] _cookies, _form, _header;
            private readonly string _headerName;
            private readonly KeyValuePair<string, string>[] _parsedCookies;
            private readonly IRequestCookiesFeature _cookieFeature;
            private readonly string _method, _scheme;
            internal object Cache;
            internal bool Active, Failed;
            internal State(HttpContext context, IAntiforgery service, string headerName)
            {
                if (context.Features[CacheFeature] != null)
                    throw new HttpAntiForgeryException("Native antiforgery state predates the MVC validation boundary.");
                _headerName = headerName; _header = HeaderToken(context, headerName);
                _service = service; _principal = new NativePrincipalSnapshot(context);
                _cookies = context.Request.Headers.Cookie.ToArray();
                _parsedCookies = Cookies(context);
                _cookieFeature = context.Features.Get<IRequestCookiesFeature>();
                _form = FormToken(context); _method = context.Request.Method; _scheme = context.Request.Scheme;
            }
            internal void Check(HttpContext context, IAntiforgery service, string headerName)
            {
                if (Active || Failed || _headerName != headerName || !_header.SequenceEqual(HeaderToken(context, headerName)) || !ReferenceEquals(_service, service) ||
                    !ReferenceEquals(Cache, context.Features[CacheFeature]) ||
                    _method != context.Request.Method || _scheme != context.Request.Scheme ||
                    !_cookies.SequenceEqual(context.Request.Headers.Cookie.ToArray()) ||
                    !ReferenceEquals(_cookieFeature, context.Features.Get<IRequestCookiesFeature>()) ||
                    !_parsedCookies.SequenceEqual(Cookies(context)) ||
                    !_form.SequenceEqual(FormToken(context)))
                    throw new HttpAntiForgeryException("Antiforgery request state changed or validation ownership was violated.");
                _principal.Check(context);
            }
            private static string[] HeaderToken(HttpContext context, string name) => name == null
                ? Array.Empty<string>() : context.Request.Headers[name].ToArray();
            private static string[] FormToken(HttpContext context) => context.Request.HasFormContentType
                ? context.Request.Form["__RequestVerificationToken"].ToArray() : Array.Empty<string>();
            private static KeyValuePair<string, string>[] Cookies(HttpContext context) => context.Request.Cookies
                .OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
        }
    }
}
