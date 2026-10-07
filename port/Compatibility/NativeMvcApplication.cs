using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using System.Web.SessionState;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MvcNet10Host;

namespace AspNetWebStack.Native;

// Optional scoped application transaction. Stage changes during the action;
// Commit must validate inside its atomic store operation immediately before
// changing state, and leave no change if it throws. DI owns its disposal.
// This is an integration contract, not a database transaction implementation.
public interface INativeMvcTransaction
{
    bool HasChanges { get; }
    void Commit(Action validateRequest);
}

// One application with explicit assemblies, native routing and synchronous/awaited MVC actions.
// Native services own authentication, authorization, CSRF and protection keys.
// Explicit policy is mandatory for every request; every mutation validates CSRF.
public sealed class NativeMvcApplication : IDisposable
{
    private readonly NativePathHosting _paths;
    private readonly NativeCompiledRazorViewEngine _views;
    private readonly NativeControllerCatalog _catalog;
    private readonly string _policy, _cookie, _applicationPath;
    private readonly string _jsonAntiforgeryHeader;
    private readonly Action<string, string> _validate;
    private bool _mapped, _disposed;
    private readonly int _maximumFileBytes, _fileMemoryThreshold;
    private readonly string _fileTempDirectory;
    private readonly FormOptions _multipartOptions;
    private readonly NativeSessionOptions _sessionOptions;
    private readonly string _childCacheNamespace = Guid.NewGuid().ToString("N");
    private readonly int _sessionTimeout;
    private readonly NativeOutputCacheOptions _outputCacheOptions;

    public NativeMvcApplication(IServiceProvider services, Assembly applicationAssembly,
        string authorizationPolicy, string tempDataCookieName, Action<string, string> validateInput)
        : this(services, applicationAssembly, authorizationPolicy, tempDataCookieName, validateInput, "/") { }

    public NativeMvcApplication(IServiceProvider services, Assembly applicationAssembly,
        string authorizationPolicy, string tempDataCookieName, Action<string, string> validateInput, string applicationVirtualPath)
        : this(services, applicationAssembly, authorizationPolicy, tempDataCookieName, validateInput, applicationVirtualPath, 8 * 1024 * 1024, 32 * 1024) { }

    public NativeMvcApplication(IServiceProvider services, Assembly applicationAssembly,
        string authorizationPolicy, string tempDataCookieName, Action<string, string> validateInput, string applicationVirtualPath,
        int maximumFileBytes, int fileMemoryThreshold)
        : this(services, applicationAssembly, authorizationPolicy, tempDataCookieName, validateInput, applicationVirtualPath,
            maximumFileBytes, fileMemoryThreshold, NativeMultipartForm.Defaults()) { }

    public NativeMvcApplication(IServiceProvider services, Assembly applicationAssembly,
        string authorizationPolicy, string tempDataCookieName, Action<string, string> validateInput, string applicationVirtualPath,
        int maximumFileBytes, int fileMemoryThreshold, FormOptions multipartOptions)
        : this(services, new[] { applicationAssembly ?? throw new ArgumentNullException(nameof(applicationAssembly)) },
            authorizationPolicy, tempDataCookieName, validateInput, applicationVirtualPath, maximumFileBytes, fileMemoryThreshold, multipartOptions) { }

    // One application may compose explicitly supplied feature assemblies. Native
    // reflection snapshots metadata; original MVC still owns controller lookup.
    public static NativeMvcApplication FromAssemblies(IServiceProvider services, IEnumerable<Assembly> applicationAssemblies,
        string authorizationPolicy, string tempDataCookieName, Action<string, string> validateInput,
        string applicationVirtualPath = "/", int maximumFileBytes = 8 * 1024 * 1024, int fileMemoryThreshold = 32 * 1024,
        FormOptions multipartOptions = null)
    {
        ArgumentNullException.ThrowIfNull(applicationAssemblies);
        var snapshot = applicationAssemblies.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(assembly => assembly == null))
            throw new ArgumentException("Supply at least one non-null application assembly.", nameof(applicationAssemblies));
        return new NativeMvcApplication(services, snapshot.Distinct().ToArray(), authorizationPolicy, tempDataCookieName,
            validateInput, applicationVirtualPath, maximumFileBytes, fileMemoryThreshold, multipartOptions ?? NativeMultipartForm.Defaults());
    }

    private NativeMvcApplication(IServiceProvider services, Assembly[] applicationAssemblies,
        string authorizationPolicy, string tempDataCookieName, Action<string, string> validateInput, string applicationVirtualPath,
        int maximumFileBytes, int fileMemoryThreshold, FormOptions multipartOptions)
    {
        _multipartOptions = NativeMultipartForm.Snapshot(multipartOptions);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFileBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(fileMemoryThreshold);
        if (fileMemoryThreshold > maximumFileBytes) throw new ArgumentOutOfRangeException(nameof(fileMemoryThreshold));
        _maximumFileBytes = maximumFileBytes; _fileMemoryThreshold = fileMemoryThreshold;
        _fileTempDirectory = Environment.GetEnvironmentVariable("ASPNETCORE_TEMP") ?? System.IO.Path.GetTempPath();
        if (!System.IO.Directory.Exists(_fileTempDirectory)) throw new System.IO.DirectoryNotFoundException("The native file buffer directory must exist at startup.");
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(authorizationPolicy); ArgumentException.ThrowIfNullOrEmpty(tempDataCookieName);
        ArgumentNullException.ThrowIfNull(validateInput);
        _outputCacheOptions = services.GetService<NativeOutputCacheOptions>();
        if (_outputCacheOptions != null) services.GetRequiredService<Microsoft.AspNetCore.OutputCaching.IOutputCacheStore>();
        _sessionOptions = services.GetService<NativeSessionOptions>()?.Snapshot();
        if (_sessionOptions != null)
            _sessionTimeout = checked((int)Math.Ceiling(services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Builder.SessionOptions>>().Value.IdleTimeout.TotalMinutes));
        services.GetRequiredService<IHttpContextAccessor>(); // One native request context per request, including CSRF ownership.
        services.GetRequiredService<IAuthorizationService>();
        _jsonAntiforgeryHeader = services.GetService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Antiforgery.AntiforgeryOptions>>()?.Value.HeaderName;
        if (_jsonAntiforgeryHeader != null) _ = new Microsoft.Net.Http.Headers.NameValueHeaderValue(_jsonAntiforgeryHeader);
        _policy = authorizationPolicy; _cookie = tempDataCookieName; _validate = validateInput;
        _catalog = new NativeControllerCatalog(applicationAssemblies);
        _views = new NativeCompiledRazorViewEngine(CompiledViewRegistry.FromAssemblies(applicationAssemblies));
        _paths = new NativePathHosting(services, applicationVirtualPath);
        _applicationPath = applicationVirtualPath == "/" ? "" : applicationVirtualPath;
        ViewEngines.Engines.Clear(); ViewEngines.Engines.Add(_views);
        System.Web.WebPages.Scope.ScopeStorage.CurrentProvider = new NativeRequestScopeStorageProvider();
        // Original PreApplicationStartCode connects every nested ViewContext to the
        // current WebPages scope. Native startup supplies its AsyncLocal provider.
        ViewContext.GlobalScopeThunk = () => System.Web.WebPages.Scope.ScopeStorage.CurrentScope;
    }

    public void Map(IEndpointRouteBuilder endpoints, string routeName, string pattern, string defaultController, string defaultAction)
    {
        Map(endpoints, routes => routes.MapRoute(routeName, pattern, new { controller = defaultController, action = defaultAction }));
    }

    public void Map(IEndpointRouteBuilder endpoints, Action<System.Web.Routing.RouteCollection> configureRoutes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(configureRoutes);
        if (_mapped) throw new InvalidOperationException("Map this application once during startup.");
        var routes = System.Web.Routing.RouteTable.Routes;
        routes.NativeCatalog = _catalog;
        configureRoutes(routes);
        NativeRouteBinding.Bind(routes, endpoints, Dispatch, acceptForms: true);
        _mapped = true;
    }

    private async Task Dispatch(Microsoft.AspNetCore.Http.HttpContext core)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var abort = core.RequestAborted;
        NativeRequestLifetime lifetime = null;
        var principal = new NativePrincipalSnapshot(core);
        var services = core.RequestServices;
        var bodyStream = core.Request.Body;
        var scheme = core.Request.Scheme; var host = core.Request.Host;
        var method = core.Request.Method; var path = core.Request.Path; var pathBase = core.Request.PathBase;
        var query = core.Request.QueryString; var contentType = core.Request.ContentType;
        var endpoint = core.GetEndpoint(); var routeValues = core.Request.RouteValues;
        var originalRouteValues = routeValues.ToArray();
        var requestFeature = core.Features.Get<IHttpRequestFeature>();
        var responseFeature = core.Features.Get<IHttpResponseFeature>();
        var reasonPhrase = responseFeature.ReasonPhrase;
        var responseStream = core.Response.Body;
        var responseBodyFeature = core.Features.Get<IHttpResponseBodyFeature>();
        var responseHeaders = core.Response.Headers;
        var originalHeaders = responseHeaders.ToDictionary(header => header.Key, header => header.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
        void Check()
        {
            abort.ThrowIfCancellationRequested(); principal.Check(core);
            lifetime?.CheckPublication();
            RouteTable.Routes.NativeBinding.ValidateUnchanged();
            if (core.Request.PathBase.Value != _applicationPath && !(String.IsNullOrEmpty(core.Request.PathBase.Value) && _applicationPath == ""))
                throw new HttpException(404, "The request PathBase does not match the configured MVC application path.");
            if (!ReferenceEquals(endpoint, core.GetEndpoint()) || !ReferenceEquals(routeValues, core.Request.RouteValues) ||
                routeValues.Count != originalRouteValues.Length || originalRouteValues.Any(pair => !routeValues.TryGetValue(pair.Key, out var value) || !Equals(value, pair.Value)))
                throw new InvalidOperationException("The matched native route changed during MVC dispatch.");
            if (_disposed || abort != core.RequestAborted || core.Response.HasStarted || scheme != core.Request.Scheme || host != core.Request.Host ||
                !ReferenceEquals(services, core.RequestServices) || !ReferenceEquals(bodyStream, core.Request.Body) ||
                !ReferenceEquals(requestFeature, core.Features.Get<IHttpRequestFeature>()) ||
                !ReferenceEquals(responseFeature, core.Features.Get<IHttpResponseFeature>()) ||
                reasonPhrase != responseFeature.ReasonPhrase ||
                !ReferenceEquals(responseBodyFeature, core.Features.Get<IHttpResponseBodyFeature>()) ||
                !ReferenceEquals(responseStream, core.Response.Body) || !ReferenceEquals(responseHeaders, core.Response.Headers) ||
                method != core.Request.Method || path != core.Request.Path || pathBase != core.Request.PathBase ||
                query != core.Request.QueryString || contentType != core.Request.ContentType)
                throw new InvalidOperationException("The owned MVC request changed before completion.");
        }
        var originalCookies = core.Response.Headers.SetCookie;
        try
        {
            Check();
            var authorized = await services.GetRequiredService<IAuthorizationService>().AuthorizeAsync(core.User, core, _policy);
            Check();
            if (!authorized.Succeeded)
            {
                if (core.User.Identity?.IsAuthenticated == true) await core.ForbidAsync(); else await core.ChallengeAsync();
                return;
            }
            // HTTP reads reach the actual MVC lifecycle, including RequireHttps.
            // Mutation preparation and its mandatory antiforgery gate remain HTTPS-only.
            if (!String.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase) && !String.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase))
                throw new HttpException(400, "The native MVC host requires HTTP or HTTPS.");
            if (String.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase) && !NativeRequestMethods.IsRead(method))
                throw new HttpException(400, "Native MVC mutation requests require HTTPS.");
            bool jsonMutation = NativeRequestMethods.IsMutation(method) && Microsoft.Net.Http.Headers.MediaTypeHeaderValue.TryParse(contentType, out var media) &&
                String.Equals(media.MediaType.Value, "application/json", StringComparison.OrdinalIgnoreCase);
            if (NativeRequestMethods.IsMutation(method) && !HttpMethods.IsPost(method) && !jsonMutation)
                throw new HttpException(415, "PUT, PATCH and DELETE require application/json in this profile.");
            if (jsonMutation && _jsonAntiforgeryHeader == null)
                throw new HttpException(415, "Configure a native antiforgery header to enable application JSON mutations.");
            NativeJsonInput jsonInput = jsonMutation ? await NativeJsonInput.ReadAsync(core, _validate, _sessionOptions != null, applicationMethods: true) : null;
            using NativeFormInput input = HttpMethods.IsPost(method) && !jsonMutation
                ? NativeMultipartForm.IsMultipart(contentType) ? await NativeMultipartForm.ReadAsync(core, _multipartOptions, _sessionOptions != null) : await NativeFormInput.ReadAsync(core, _sessionOptions != null)
                : null;
            bool preparedMutation = input != null || jsonInput != null;
            Check();
            using var requestLifetime = lifetime = new NativeRequestLifetime(core, services.GetRequiredService<IHttpContextAccessor>(), _jsonAntiforgeryHeader, _sessionOptions != null);
            lifetime.ChildCacheNamespace = _childCacheNamespace;
            lifetime.OutputCacheOptions = _outputCacheOptions;
            lifetime.ValidateOutputCacheOwner = Check;
            var response = new NativeBufferedResponse(abort, lifetime, _maximumFileBytes, _fileMemoryThreshold, _fileTempDirectory);
            try
            {
                byte[] body = null; int status; string type, location;
                try
                {
                    HttpContextBase context = jsonInput != null ? new NativeJsonContext(core, response, jsonInput, _validate, 8192, 256, lifetime)
                        : input == null ? new NativeQueryContext(core, response, _validate, 8192, 256, null, null, lifetime: lifetime)
                        : new NativeFormContext(core, response, input, _validate, _validate, 8192, 256, lifetime);
                    using ((IDisposable)context)
                    using (var tempData = new NativeCookieTempDataProvider(core, services.GetRequiredService<IDataProtectionProvider>(),
                        services.GetRequiredService<ILoggerFactory>(), _cookie, 2048, 32, lifetime))
                    {
                        NativeAuthorization.CheckPrincipal(context);
                        if (preparedMutation) NativeAntiforgery.Validate(context);
                        var factory = new SessionFactory(_catalog.CreateFactory(new NativeControllerActivator(services, controller =>
                        {
                            var mvc = (Controller)controller;
                            NativeLegacyAsyncOperation.ValidateController(mvc);
                            mvc.ActionInvoker = new NativeAsyncActionInvoker(_catalog.Descriptors); mvc.TempDataProvider = tempData;
                        })), _sessionOptions != null);
                        var route = System.Web.Routing.RouteTable.Routes.GetRouteData(context)
                            ?? throw new HttpException(404, "No MVC route matched.");
                        var execution = new System.Web.Routing.RequestContext(context, route);
                        var sessionMode = factory.GetControllerSessionBehavior(execution, route.GetRequiredString("controller"));
                        if (sessionMode != SessionStateBehavior.Disabled && _sessionOptions != null)
                            lifetime.Session = await NativeSessionState.LoadAsync(core, lifetime, _sessionOptions, sessionMode, _sessionTimeout);
                        lifetime.ChildFactory = factory;
                        await ControllerExecution.ExecuteAsync(execution, factory);
                        Check();
                        long length = response.BufferedLength;
                        if (!response.IsBinary) body = response.GetBytes();
                        status = response.StatusCode;
                        type = response.IsBinary ? response.ContentType : response.ContentType + "; charset=" + response.ContentEncoding.WebName;
                        location = response.Location;
                        if (status < 200 || status > 599 || !Microsoft.Net.Http.Headers.MediaTypeHeaderValue.TryParse(type, out _) ||
                            ((status == 204 || status == 205 || status == 304) && length != 0))
                            throw new InvalidOperationException("MVC produced an unsupported buffered response status or content type.");
                        // Validate and stage native response metadata before the
                        // transaction. Only network publication remains afterward.
                        core.Response.StatusCode = status; core.Response.ContentType = type;
                        if (response.TrySkipIisCustomErrors && core.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodePagesFeature>() is { } statusPages)
                            statusPages.Enabled = false;
                        core.Response.ContentLength = status == 204 || status == 304 ? null : length;
                        if (response.DownloadDisposition != null) core.Response.Headers.ContentDisposition = response.DownloadDisposition;
                        if (location != null) core.Response.Headers.Location = location;
                        var preparedHeaders = responseHeaders.Where(header => !String.Equals(header.Key, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
                            .ToDictionary(header => header.Key, header => header.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
                        var transaction = services.GetService<INativeMvcTransaction>();
                        bool pending = transaction?.HasChanges == true;
                        if (pending && (!preparedMutation || status < 200 || status >= 400 || status == 304))
                            throw new InvalidOperationException("Changes require a successful protected mutation request.");
                        bool sessionPending = status >= 200 && status < 400 && lifetime.Session?.Prepare() == true;
                        if (pending && sessionPending)
                            throw new InvalidOperationException("Session and business-store writes require separate requests or a shared transaction contract.");
                        if (status >= 200 && status < 400) tempData.Commit();
                        void ValidateCommit()
                        {
                            Check();
                            var headers = responseHeaders.Where(header => !String.Equals(header.Key, "Set-Cookie", StringComparison.OrdinalIgnoreCase));
                            if (core.Response.StatusCode != status || headers.Count() != preparedHeaders.Count ||
                                headers.Any(header => !preparedHeaders.TryGetValue(header.Key, out var values) || !header.Value.SequenceEqual(values)))
                                throw new InvalidOperationException("The prepared MVC response changed before commit.");
                            _ = ((INativeRoutingContext)context).CoreContext;
                            if (preparedMutation) NativeAntiforgeryOwnership.Run(core, services.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>(), () => true, _jsonAntiforgeryHeader);
                        }
                        ValidateCommit();
                        if (sessionPending) { await lifetime.Session.CommitAsync(); ValidateCommit(); }
                        if (pending) transaction.Commit(ValidateCommit);
                        if (status == 200)
                        {
                            ValidateCommit(); lifetime.PublishChildCache();
                            if (lifetime.OutputCache != null)
                            {
                                await lifetime.OutputCache.PublishAsync(response, ValidateCommit);
                                ValidateCommit();
                            }
                        }
                    }
                }
                finally { response.Complete(); }
                if (status == 401)
                {
                    core.Response.Headers.SetCookie = originalCookies;
                    core.Response.ContentLength = null; core.Response.ContentType = null; core.Response.Headers.Remove("Location"); core.Response.Headers.Remove("Content-Disposition");
                    if (core.User.Identity?.IsAuthenticated == true) await core.ForbidAsync(); else await core.ChallengeAsync();
                    return;
                }
                if (!HttpMethods.IsHead(method) && status != 204 && status != 205 && status != 304)
                {
                    if (response.IsBinary) await response.DrainAsync(core.Response.Body, abort);
                    else await core.Response.Body.WriteAsync(body, abort);
                }
            }
            finally { await response.DisposeFileAsync(); }
        }
        catch (Exception error) when (!core.Response.HasStarted)
        {
            if (abort.IsCancellationRequested) { core.Abort(); return; }
            // A rejected callback may have replaced the body or headers. Restore
            // the owned transport and initial headers before publishing an error.
            // A foreign native response feature cannot safely be repaired here.
            if (!ReferenceEquals(responseFeature, core.Features.Get<IHttpResponseFeature>())) { core.Abort(); return; }
            core.Features.Set(responseBodyFeature); responseFeature.Headers = responseHeaders; responseFeature.ReasonPhrase = reasonPhrase;
            responseHeaders.Clear();
            foreach (var header in originalHeaders) responseHeaders[header.Key] = header.Value;
            core.Response.StatusCode = error is HttpAntiForgeryException ? 400 : error is HttpException http ? http.GetHttpCode()
                : error is PlatformNotSupportedException ? 501 : 500;
            core.Response.Headers.Remove("Location");
            var bytes = Encoding.UTF8.GetBytes(error.GetType().Name + "\n");
            core.Response.ContentType = "text/plain; charset=utf-8"; core.Response.ContentLength = bytes.Length;
            services.GetRequiredService<ILogger<NativeMvcApplication>>().LogWarning(error, "MVC dispatch failed before response publication.");
            await core.Response.Body.WriteAsync(bytes, abort);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _views.Dispose(); _paths.Dispose();
    }
    private sealed class SessionFactory : IControllerFactory
    {
        private readonly IControllerFactory _inner;
        private readonly bool _enabled;
        internal SessionFactory(IControllerFactory inner, bool enabled) { _inner = inner; _enabled = enabled; }
        public IController CreateController(System.Web.Routing.RequestContext context, string name) => _inner.CreateController(context, name);
        public void ReleaseController(IController controller) => _inner.ReleaseController(controller);
        public SessionStateBehavior GetControllerSessionBehavior(System.Web.Routing.RequestContext context, string name)
        { var mode = _inner.GetControllerSessionBehavior(context, name); return mode == SessionStateBehavior.Default ? (_enabled ? SessionStateBehavior.Required : SessionStateBehavior.Disabled) : mode; }
    }
}
