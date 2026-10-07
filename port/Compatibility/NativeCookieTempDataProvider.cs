using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using CookieTempDataProviderOptions = Microsoft.AspNetCore.Mvc.CookieTempDataProviderOptions;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AspNetWebStack.Native
{
    // One MVC execution borrows this provider (synchronous by public construction;
    // internally opt-in awaited lifetime for NativeMvcApplication). Original MVC owns
    // TempDataDictionary consumption; the host commits only after successful
    // result execution and controller release, before publishing its response.
    public sealed class NativeCookieTempDataProvider : System.Web.Mvc.ITempDataProvider, IDisposable
    {
        private readonly Microsoft.AspNetCore.Http.HttpContext _core;
        private readonly CookieTempDataProvider _provider;
        private readonly NativeTempDataSerializer _serializer;
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private readonly NativeRequestLifetime _lifetime;
        private readonly string[] _headers;
        private readonly KeyValuePair<string, string>[] _cookies;
        private readonly IRequestCookiesFeature _cookieFeature;
        private readonly string _pathBase, _scheme;
        private readonly bool _incoming;
        private ControllerContext _owner;
        private HttpContextBase _ownerHttpContext;
        private IDictionary<string, object> _saved;
        private bool _loaded, _staged, _committed, _disposed, _busy;
        private volatile bool _faulted;

        public NativeCookieTempDataProvider(Microsoft.AspNetCore.Http.HttpContext context,
            IDataProtectionProvider protection, ILoggerFactory loggerFactory,
            string cookieName = "__Host-MvcPort.TempData", int maximumSerializedBytes = 2048, int maximumKeys = 32)
            : this(context, protection, loggerFactory, cookieName, maximumSerializedBytes, maximumKeys, null) { }
        internal NativeCookieTempDataProvider(Microsoft.AspNetCore.Http.HttpContext context,
            IDataProtectionProvider protection, ILoggerFactory loggerFactory,
            string cookieName, int maximumSerializedBytes, int maximumKeys, NativeRequestLifetime lifetime)
        {
            _lifetime = lifetime;
            _core = context ?? throw new ArgumentNullException(nameof(context));
            ArgumentNullException.ThrowIfNull(protection);
            ArgumentNullException.ThrowIfNull(loggerFactory);
            if (String.IsNullOrEmpty(cookieName) || !cookieName.StartsWith("__Host-", StringComparison.Ordinal) ||
                cookieName.Length > 96 || cookieName.Any(c => !(Char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_' || c == '.')))
                throw new ArgumentException("Use a distinct __Host- cookie name of at most 96 ASCII token characters.", nameof(cookieName));
            _serializer = new NativeTempDataSerializer(maximumSerializedBytes, maximumKeys);
            _scheme = _core.Request.Scheme;
            if (!String.Equals(_scheme, "http", StringComparison.OrdinalIgnoreCase) && !String.Equals(_scheme, "https", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Cookie TempData requires HTTP or HTTPS.");
            _headers = _core.Request.Headers.Cookie.ToArray();
            int remaining = 16384;
            foreach (string header in _headers)
            {
                if (header.Length > remaining) throw new HttpException(400, "Cookie headers exceed this TempData boundary.");
                remaining -= header.Length;
            }
            var wire = _core.Request.GetTypedHeaders().Cookie;
            // Native Request.Cookies lookup is case-insensitive. Reject aliases
            // before delegating so admission, load and deletion identify the
            // same single wire cookie, and chunk checks cannot be bypassed.
            if (wire.Any(c => c.Name.Value.StartsWith(cookieName + "C", StringComparison.OrdinalIgnoreCase)))
                throw new HttpException(400, "Chunked TempData cookies are outside this boundary.");
            var own = wire.Where(c => String.Equals(c.Name.Value, cookieName, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (own.Length > 1 || (own.Length == 1 &&
                (!String.Equals(own[0].Name.Value, cookieName, StringComparison.Ordinal) || own[0].Value.Length > 4096)))
                throw new HttpException(400, "TempData requires at most one bounded cookie with its exact configured name.");
            _incoming = own.Length == 1;
            _cookies = Cookies(_core);
            _cookieFeature = _core.Features.Get<IRequestCookiesFeature>();
            var headersOnly = new DefaultHttpContext(); headersOnly.Request.Headers.Cookie = _core.Request.Headers.Cookie;
            if (!_cookies.SequenceEqual(Cookies(headersOnly))) throw new InvalidOperationException("Cookie features must agree with the received headers.");
            _pathBase = _core.Request.PathBase.Value;
            var options = new CookieTempDataProviderOptions();
            options.Cookie.Name = cookieName; options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true; options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict;
            // Consent remains the host's CookiePolicy decision; not marked essential.
            options.Cookie.IsEssential = false;
            // Native protection, with an additional purpose binding the cookie name
            // and this deliberately restricted serialization schema.
            _provider = new CookieTempDataProvider(protection.CreateProtector("AspNetWebStack.Native.TempData", cookieName, "v1"),
                loggerFactory, Options.Create(options), _serializer);
        }

        public IDictionary<string, object> LoadTempData(ControllerContext controllerContext) => Run(() =>
        {
            var httpContext = CheckController(controllerContext);
            if (_loaded) throw new InvalidOperationException("TempData can be loaded once per owned execution.");
            _owner = controllerContext; _ownerHttpContext = httpContext;
            // Invalid native cookies are best-effort empty data. Native deletion
            // is isolated until the host commits, so failed dispatch publishes none.
            var request = new DefaultHttpContext(); request.Request.Scheme = _scheme;
            request.Request.Headers.Cookie = _headers;
            var values = _provider.LoadTempData(request);
            _loaded = true;
            return values;
        });

        public void SaveTempData(ControllerContext controllerContext, IDictionary<string, object> values) => Run(() =>
        {
            CheckController(controllerContext);
            if (!_loaded || !ReferenceEquals(_owner, controllerContext) || _staged)
                throw new InvalidOperationException("TempData requires one save after its matching load.");
            ArgumentNullException.ThrowIfNull(values);
            // Copy through the bounded scalar codec now. No user-owned mutable
            // dictionary or objects survive until Commit.
            _saved = _serializer.Deserialize(_serializer.Serialize(values));
            _staged = true;
            return true;
        });

        public void Commit()
        {
            Microsoft.Extensions.Primitives.StringValues previous = default;
            bool captured = false;
            try
            {
                Run(() =>
                {
                    if (!_staged || _committed) throw new InvalidOperationException("Commit requires one completed TempData save.");
                    CheckController(_owner);
                    previous = _core.Response.Headers.SetCookie;
                    captured = true;
                    // Use the actual response cookie feature, preserving native
                    // CookiePolicy/consent callbacks. No raw Set-Cookie bypass.
                    if (_incoming || _saved.Count != 0) _provider.SaveTempData(_core, _saved);
                    _committed = true;
                    return true;
                });
            }
            catch
            {
                if (captured && !_core.Response.HasStarted) _core.Response.Headers.SetCookie = previous;
                throw;
            }
        }

        public void Dispose()
        {
            if (_lifetime != null) _lifetime.CheckIdentity();
            else if (Environment.CurrentManagedThreadId != _thread)
            {
                _faulted = true;
                throw new InvalidOperationException("TempData disposal requires its owner thread.");
            }
            _disposed = true; _saved = null;
        }

        private T Run<T>(Func<T> operation)
        {
            try
            {
                CheckAccess();
                if (_busy || _faulted) throw new InvalidOperationException("TempData execution ownership was violated.");
                _busy = true;
                try
                {
                    T value = operation();
                    CheckAccess();
                    // Native cookie callbacks and user dictionary enumeration
                    // may dispose or replace the MVC wrapper while an operation
                    // runs. Recheck the original wrapper before publishing success.
                    if (_owner != null) CheckController(_owner);
                    if (_faulted) throw new InvalidOperationException("TempData execution ownership was violated.");
                    return value;
                }
                finally { _busy = false; }
            }
            catch { _faulted = true; throw; }
        }

        private HttpContextBase CheckController(ControllerContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            var httpContext = context.HttpContext;
            if (_owner != null && (!ReferenceEquals(context, _owner) || !ReferenceEquals(httpContext, _ownerHttpContext)))
                throw new InvalidOperationException("TempData requires its original controller context and native wrapper.");
            if (httpContext is not INativeRoutingContext native || !ReferenceEquals(native.CoreContext, _core))
                throw new InvalidOperationException("TempData must use its original native request boundary.");
            return httpContext;
        }

        private void CheckAccess()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(NativeCookieTempDataProvider));
            if (_lifetime != null) _lifetime.CheckIdentity();
            else if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("TempData requires its synchronous owner thread.");
            _core.RequestAborted.ThrowIfCancellationRequested();
            if (_core.Response.HasStarted || _core.Request.Scheme != _scheme || _core.Request.PathBase.Value != _pathBase ||
                !_headers.SequenceEqual(_core.Request.Headers.Cookie.ToArray()) ||
                !ReferenceEquals(_cookieFeature, _core.Features.Get<IRequestCookiesFeature>()) || !_cookies.SequenceEqual(Cookies(_core)))
                throw new InvalidOperationException("TempData request cookies or response ownership changed.");
        }

        private static KeyValuePair<string, string>[] Cookies(Microsoft.AspNetCore.Http.HttpContext context) =>
            context.Request.Cookies.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray();
    }
}
