using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Security.Principal;
using System.Text;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AspNetWebStack.Native
{
    // A body-free request boundary: public construction is synchronous; the native
    // application internally opts into sequential awaited ownership. The host supplies a deliberate
    // value-validation policy: this does not impersonate System.Web RequestValidator.
    public sealed class NativeQueryContext : HttpContextBase, INativeRoutingContext, IDisposable
    {
        private readonly Microsoft.AspNetCore.Http.HttpContext _core;
        private readonly HttpContextBase _adapter;
        private readonly HttpResponseBase _response;
        private readonly QueryRequest _request;
        private bool? _customErrors;

        public NativeQueryContext(Microsoft.AspNetCore.Http.HttpContext core, HttpResponseBase response,
            Action<string, string> validateQueryValue, int maximumQueryLength = 8192, int maximumQueryValues = 256)
            : this(core, response, validateQueryValue, maximumQueryLength, maximumQueryValues, null, null)
        {
        }

        internal NativeQueryContext(Microsoft.AspNetCore.Http.HttpContext core, HttpResponseBase response,
            Action<string, string> validateQueryValue, int maximumQueryLength, int maximumQueryValues,
            NativeFormInput formInput, Action<string, string> validateFormValue, NativeJsonInput jsonInput = null, NativeRequestLifetime lifetime = null)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
            _response = response ?? throw new ArgumentNullException(nameof(response));
            ArgumentNullException.ThrowIfNull(validateQueryValue);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumQueryLength);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumQueryValues);
            _adapter = core;
            _request = new QueryRequest(core, validateQueryValue, maximumQueryLength, maximumQueryValues, formInput, validateFormValue, jsonInput, lifetime);
        }

        Microsoft.AspNetCore.Http.HttpContext INativeRoutingContext.CoreContext { get { _request.CheckAccess(); return _core; } }
        public override HttpRequestBase Request { get { _request.CheckAccess(); return _request; } }
        public override HttpResponseBase Response { get { _request.CheckAccess(); return _response; } }
        public override IDictionary Items { get { _request.CheckAccess(); return _adapter.Items; } }
        public override IPrincipal User { get { _request.CheckAccess(); return _core.User; } set => throw new PlatformNotSupportedException("The native host owns the request principal."); }
        public override HttpSessionStateBase Session { get { _request.CheckAccess(); return NativeLifetime?.Session; } }
        public override bool IsCustomErrorEnabled
        {
            get
            {
                _request.CheckAccess();
                if (!_customErrors.HasValue)
                {
                    var environment = _core.RequestServices?.GetService<IHostEnvironment>();
                    _customErrors = environment != null && !environment.IsDevelopment();
                }
                return _customErrors.Value;
            }
        }
        internal NativeRequestLifetime NativeLifetime => _request.Lifetime;
        public void Dispose() => _request.Complete(); // Response, services and native context remain host-owned.

        internal static void ValidateUrlEncodedInput(string query, string inputName)
            {
                if (String.IsNullOrEmpty(query)) return;
                // Framework's parser treats key-only fields as null-key values;
                // the native parser treats them as empty values under that key.
                // Reject this ambiguous wire form rather than silently rebinding it.
                foreach (string part in query.Substring(1).Split('&'))
                    if (part.Length > 0 && part.IndexOf('=') < 0)
                        throw new HttpException(400, $"Key-only {inputName} fields are unsupported; use key=value.");
                // Binding still uses the native parser. This admission check only
                // rejects legacy %u escapes and malformed percent/UTF-8 sequences
                // whose replacement behavior differs between parser generations.
                for (int i = 0; i < query.Length; i++)
                {
                    if (query[i] != '%') continue;
                    var bytes = new System.Collections.Generic.List<byte>();
                    while (i < query.Length && query[i] == '%')
                    {
                        if (i + 2 >= query.Length || !Byte.TryParse(query.AsSpan(i + 1, 2), System.Globalization.NumberStyles.AllowHexSpecifier,
                            System.Globalization.CultureInfo.InvariantCulture, out byte value))
                            throw new HttpException(400, $"Malformed {inputName} percent encoding.");
                        bytes.Add(value); i += 3;
                    }
                    try { new UTF8Encoding(false, true).GetString(bytes.ToArray()); }
                    catch (DecoderFallbackException) { throw new HttpException(400, $"Malformed {inputName} UTF-8 encoding."); }
                    i--;
                }
            }

        private sealed class QueryRequest : HttpRequestBase, INet10ValidatedRequest, INativeQueryRequest, INativeFormRequest
        {
            private readonly Microsoft.AspNetCore.Http.HttpContext _core;
            private readonly HttpRequestBase _adapter;
            private readonly int _thread = Environment.CurrentManagedThreadId;
            internal NativeRequestLifetime Lifetime { get; }
            private readonly QueryCollection _validated;
            private readonly QueryCollection _unvalidated;
            private bool _completed;
            private readonly NativePrincipalSnapshot _principal;
            private readonly QueryCollection _validatedForm, _unvalidatedForm;
            private readonly NativeFormInput _formInput;
            private readonly HttpFileCollectionBase _files;
            private readonly NativeJsonInput _jsonInput;
            private readonly System.IO.Stream _jsonStream;
            private HttpCookieCollection _cookies;
            private LookupCollection _parameters, _serverVariables;

            internal QueryRequest(Microsoft.AspNetCore.Http.HttpContext core, Action<string, string> validate, int maxLength, int maxValues, NativeFormInput formInput, Action<string, string> validateFormValue, NativeJsonInput jsonInput, NativeRequestLifetime lifetime)
            {
                Lifetime = lifetime;
                _core = core;
                _principal = new NativePrincipalSnapshot(core);
                _adapter = core.Request;
                CheckAccess();
                var request = core.Request;
                if (formInput == null && jsonInput == null && (!(lifetime != null ? NativeRequestMethods.IsRead(request.Method) : HttpMethods.IsGet(request.Method)) || request.ContentLength.GetValueOrDefault() != 0 ||
                    request.ContentType != null || core.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true))
                    throw new PlatformNotSupportedException("This boundary requires a body-free admitted read method; forms, JSON and uploads require prepared input.");
                if (core.Features.Get<ISessionFeature>() != null && lifetime?.SessionEnabled != true)
                    throw new PlatformNotSupportedException("NativeQueryContext requires session to be disabled.");
                if (request.QueryString.Value?.Length > maxLength) throw new HttpException(414, "Query length limit exceeded.");
                ValidateUrlEncodedInput(request.QueryString.Value, "query");
                // Read the current native collection directly: an adapter may have
                // cached an earlier collection before host middleware rewrote Query.
                var source = request.Query;
                if (source.Count > maxValues) throw new HttpException(400, "Query key count limit exceeded.");
                // The native parser can retain a later duplicate's key spelling.
                // Original dictionary binding observes AllKeys, so recover the first
                // decoded spelling with the same native decoder, without reparsing values.
                var firstNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in new QueryStringEnumerable(request.QueryString.Value))
                {
                    string name = pair.DecodeName().ToString();
                    firstNames.TryAdd(name, name);
                }
                var snapshot = new NameValueCollection(StringComparer.OrdinalIgnoreCase);
                int count = 0;
                foreach (var pair in source)
                {
                    string key = firstNames.TryGetValue(pair.Key, out string firstName) ? firstName : pair.Key;
                    if (pair.Value.Count > maxValues - count) throw new HttpException(400, "Query value count limit exceeded.");
                    count += pair.Value.Count;
                    foreach (string value in pair.Value) snapshot.Add(key, value);
                }
                _validated = new QueryCollection(snapshot, CheckAccess, validate);
                _unvalidated = new QueryCollection(snapshot, CheckAccess, null);
                if (formInput != null)
                {
                    var form = formInput.Claim(core);
                    _formInput = formInput;
                    _files = formInput.Files(CheckAccess);
                    _validatedForm = new QueryCollection(form, CheckAccess, validateFormValue);
                    _unvalidatedForm = new QueryCollection(form, CheckAccess, null);
                }
                if (formInput == null)
                {
                    // Empty form data is owned input, not prepared form admission.
                    _validatedForm = new QueryCollection(new NameValueCollection(), CheckFormAccess, null);
                    _unvalidatedForm = _validatedForm;
                }
                if (jsonInput != null)
                {
                    if (formInput != null) throw new InvalidOperationException("A request cannot claim both form and JSON input.");
                    _jsonInput = jsonInput;
                    _jsonStream = jsonInput.Claim(core, CheckAccess, Lifetime);
                }
            }

            internal void CheckAccess()
            {
                if (Lifetime != null) Lifetime.CheckIdentity();
                else if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Query access requires the synchronous request owner.");
                ObjectDisposedException.ThrowIf(_completed, this);
                _core.RequestAborted.ThrowIfCancellationRequested();
                _principal.Check(_core);
                _jsonInput?.Check();
                _formInput?.Check();
            }
            internal void Complete()
            {
                if (Lifetime != null) Lifetime.CheckIdentity();
                else if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Query completion requires the request owner.");
                _completed = true;
                _formInput?.Dispose();
            }
            public void ValidateInput() => CheckAccess(); // Values are validated lazily by the validated collection.
            // NameValueCollection has nonvirtual bulk accessors. Validate the
            // full snapshot before exposing it publicly, including CopyTo.
            // MVC's provider receives the internal lazy collection instead.
            public override NameValueCollection QueryString { get { _validated.ValidateAll(); return _validated; } }
            NameValueCollection INativeQueryRequest.ValidatedQueryString { get { CheckAccess(); return _validated; } }
            NameValueCollection IUnvalidatedRequestValues.QueryString { get { CheckAccess(); return _unvalidated; } }
            private void CheckFormAccess()
            {
                CheckAccess();
                // Never reinterpret a later method/body mutation as prepared input.
                if (_formInput == null && _jsonInput == null &&
                    (!(Lifetime != null ? NativeRequestMethods.IsRead(_core.Request.Method) : HttpMethods.IsGet(_core.Request.Method)) ||
                     _core.Request.ContentLength.GetValueOrDefault() != 0 || _core.Request.ContentType != null ||
                     _core.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true))
                    throw new PlatformNotSupportedException("Empty Form requires an unchanged admitted body-free read or prepared JSON.");
            }
            public override NameValueCollection Form
            {
                get { CheckFormAccess(); _validatedForm.ValidateAll(); return _validatedForm; }
            }
            NameValueCollection INativeFormRequest.FormCollectionValues(bool validated)
            {
                CheckFormAccess();
                // Add(NameValueCollection) bypasses virtual accessors: validate every
                // field before the original FormCollection constructor bulk-copies it.
                if (validated) _validatedForm.ValidateAll();
                return validated ? _validatedForm : _unvalidatedForm;
            }
            bool INativeFormRequest.HasFormInput { get { CheckAccess(); return _formInput != null; } }
            NameValueCollection INativeFormRequest.ValidatedForm { get { CheckFormAccess(); return _validatedForm; } }
            NameValueCollection IUnvalidatedRequestValues.Form { get { CheckFormAccess(); return _unvalidatedForm; } }
            // First present source, matching the native adapter's selection order.
            // Query/form use the owned snapshots; cookies/server variables remain
            // live native services with their own key/value semantics. Query/form
            // validation callbacks do not apply to those two fallback sources.
            public override string this[string key] => Parameters.Get(key);
            private LookupCollection Parameters => _parameters ??= new LookupCollection(CheckAccess, GetParameterValues);
            public override NameValueCollection Params { get { CheckAccess(); return Parameters; } }
            private string[] GetParameterValues(string key)
            {
                if (key == null) return null;
                if (_validated.TryGetValues(key, out var values)) return values;
                CheckFormAccess();
                if (_validatedForm.TryGetValues(key, out values)) return values;
                if (_core.Request.Cookies.TryGetValue(key, out string value)) return new[] { value };
                return GetServerValues(key);
            }
            private string[] GetServerValues(string key)
            {
                if (key == null) return null;
                string value = _core.Features.Get<IServerVariablesFeature>()?[key];
                return value == null ? null : new[] { value };
            }
            public override NameValueCollection ServerVariables
            {
                get { CheckAccess(); return _serverVariables ??= new LookupCollection(CheckAccess, GetServerValues); }
            }
            public override HttpCookieCollection Cookies
            {
                get
                {
                    CheckAccess();
                    if (_cookies == null)
                    {
                        // The sealed public containers cannot enforce retained ownership.
                        // Copy current native parsed values once, independently of the adapter's
                        // cache. Local edits never change native authentication/CSRF/TempData input.
                        var cookies = new HttpCookieCollection();
                        foreach (var pair in _core.Request.Cookies) cookies.Add(new HttpCookie(pair.Key, pair.Value));
                        _cookies = cookies;
                    }
                    return _cookies;
                }
            }
            string IUnvalidatedRequestValues.this[string key] => throw new PlatformNotSupportedException("Combined unvalidated input has not been enabled.");
            public override HttpFileCollectionBase Files { get { CheckAccess(); return _files ?? _adapter.Files; } }
            public override System.IO.Stream InputStream { get { CheckAccess(); return _jsonStream ?? throw new PlatformNotSupportedException("Body access requires prepared native JSON input."); } }
            public override NameValueCollection Headers { get { CheckAccess(); return _adapter.Headers; } }
            public override string HttpMethod { get { CheckAccess(); return _core.Request.Method; } }
            public override string ContentType { get { CheckAccess(); return _core.Request.ContentType ?? String.Empty; } set => throw new PlatformNotSupportedException(); }
            public override string Path { get { CheckAccess(); return _core.Request.Path.Value; } }
            public override string RawUrl { get { CheckAccess(); return _core.Features.Get<IHttpRequestFeature>()?.RawTarget ?? (_core.Request.PathBase.ToUriComponent() + _core.Request.Path.ToUriComponent() + _core.Request.QueryString.Value); } }
            public override string ApplicationPath { get { CheckAccess(); return _core.Request.PathBase.HasValue ? _core.Request.PathBase.Value : "/"; } }
            // Live native URL state, like ApplicationPath and Path. The adapter
            // already uses GetEncodedUrl/Uri and Request.IsHttps; no parser or
            // request snapshot is introduced. Retained access still checks ownership.
            public override Uri Url { get { CheckAccess(); return _adapter.Url; } }
            public override bool IsSecureConnection { get { CheckAccess(); return _adapter.IsSecureConnection; } }
            public override string UserAgent { get { CheckAccess(); return _adapter.UserAgent; } }
            public override string UserHostAddress { get { CheckAccess(); return _adapter.UserHostAddress; } }
            public override string UserHostName { get { CheckAccess(); return _adapter.UserHostName; } }
            public override bool IsLocal { get { CheckAccess(); return _adapter.IsLocal; } }
            public override bool IsAuthenticated { get { CheckAccess(); return _adapter.IsAuthenticated; } }
            public override Uri UrlReferrer { get { CheckAccess(); return _adapter.UrlReferrer; } }
            // The adapter caches parsed header arrays; never expose its mutable arrays.
            public override string[] AcceptTypes { get { CheckAccess(); return (string[])_adapter.AcceptTypes.Clone(); } }
            public override string[] UserLanguages { get { CheckAccess(); return (string[])_adapter.UserLanguages.Clone(); } }
            public override int ContentLength { get { CheckAccess(); return checked((int)(_core.Request.ContentLength ?? 0)); } }
            public override Encoding ContentEncoding { get { CheckAccess(); return (Encoding)_adapter.ContentEncoding?.Clone(); } }
            public override string AppRelativeCurrentExecutionFilePath { get { CheckAccess(); return "~" + _core.Request.Path.Value; } }
        }

        // Native server variables have no enumerable key set. This is deliberately a
        // lookup surface, not an aggregate NameValueCollection. Nonvirtual BCL bulk
        // operations are outside the contract and cannot be made into owned lookups.
        private sealed class LookupCollection : NameValueCollection
        {
            private readonly Action _check;
            private readonly Func<string, string[]> _lookup;
            internal LookupCollection(Action check, Func<string, string[]> lookup)
            { _check = check; _lookup = lookup; IsReadOnly = true; }
            private PlatformNotSupportedException Unsupported()
            { _check(); return new PlatformNotSupportedException("Native request parameters and server variables support named lookup only."); }
            public override string Get(string name)
            { var values = GetValues(name); return values == null ? null : values.Length == 1 ? values[0] : String.Join(",", values); }
            public override string[] GetValues(string name) { _check(); return _lookup(name); }
            public override int Count => throw Unsupported();
            public override string[] AllKeys => throw Unsupported();
            public override KeysCollection Keys => throw Unsupported();
            public override IEnumerator GetEnumerator() => throw Unsupported();
            public override string GetKey(int index) => throw Unsupported();
            public override string Get(int index) => throw Unsupported();
            public override string[] GetValues(int index) => throw Unsupported();
            public override void Add(string name, string value) => throw Unsupported();
            public override void Set(string name, string value) => throw Unsupported();
            public override void Remove(string name) => throw Unsupported();
            public override void Clear() => throw Unsupported();
        }

        private sealed class QueryCollection : NameValueCollection
        {
            private readonly Action _check;
            private readonly Action<string, string> _validate;
            internal QueryCollection(NameValueCollection source, Action check, Action<string, string> validate) : base(source)
            { _check = check; _validate = validate; IsReadOnly = true; }
            public override int Count { get { _check(); return base.Count; } }
            public override string[] AllKeys { get { _check(); return (string[])base.AllKeys.Clone(); } }
            public override IEnumerator GetEnumerator() { _check(); return base.GetEnumerator(); }
            public override string GetKey(int index) { _check(); return base.GetKey(index); }
            public override string Get(string name) { Validate(name); return base.Get(name); }
            public override string Get(int index) { string key = GetKey(index); Validate(key); return base.Get(index); }
            public override string[] GetValues(string name) { Validate(name); return base.GetValues(name); }
            public override string[] GetValues(int index) { string key = GetKey(index); Validate(key); return base.GetValues(index); }
            private void Validate(string key)
            {
                _check();
                if (_validate != null)
                    foreach (var value in base.GetValues(key) ?? Array.Empty<string>()) _validate(key, value);
            }
            internal bool TryGetValues(string key, out string[] value)
            {
                _check();
                foreach (string name in base.AllKeys)
                    if (String.Equals(name, key, StringComparison.OrdinalIgnoreCase))
                    { value = GetValues(name); return true; }
                value = null; return false;
            }
            internal void ValidateAll() { _check(); foreach (string key in base.AllKeys) Validate(key); }
        }
    }

    internal static class NativeQueryValues
    {
        internal static IUnvalidatedRequestValues Resolve(HttpRequestBase request) => request as IUnvalidatedRequestValues
            ?? throw new PlatformNotSupportedException("Query providers require a request with explicit validated and unvalidated query collections.");
        internal static NameValueCollection Validated(HttpRequestBase request) => request is INativeQueryRequest native
            ? native.ValidatedQueryString : throw new PlatformNotSupportedException("Query providers require the native query request boundary.");
    }

    internal interface INativeQueryRequest : IUnvalidatedRequestValues
    {
        NameValueCollection ValidatedQueryString { get; }
    }
}
