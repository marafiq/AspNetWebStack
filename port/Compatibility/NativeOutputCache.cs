using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web.Mvc;
using System.Web.Routing;
using System.Web.UI;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using OutputCacheAttribute = System.Web.Mvc.OutputCacheAttribute;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace AspNetWebStack.Native;

// Host-selected namespace/tag. Storage, absolute expiry and tag eviction belong
// to AddOutputCache's IOutputCacheStore; this type is immutable configuration.
public sealed class NativeOutputCacheOptions
{
    public NativeOutputCacheOptions(string applicationTag)
    {
        ArgumentException.ThrowIfNullOrEmpty(applicationTag);
        if (applicationTag.Length > 128 || applicationTag.Any(Char.IsControl))
            throw new ArgumentException("Use a bounded application cache tag.", nameof(applicationTag));
        ApplicationTag = applicationTag;
    }
    public string ApplicationTag { get; }
}

// Compatibility key/representation translation only. No cache engine, timer,
// eviction index, output middleware, stampede lock or System.Web Page lifecycle.
internal sealed class NativeOutputCache
{
    private readonly ControllerContext _context;
    private readonly NativeRequestLifetime _lifetime;
    private readonly HttpContext _core;
    private readonly IOutputCacheStore _store;
    private readonly ILogger _logger;
    private readonly TempDataDictionary _tempData;
    private readonly int _tempVersion, _replacementVersion;
    private readonly Dictionary<string, string[]> _headers;
    private readonly string _key;
    private readonly string[] _tags;
    private readonly TimeSpan _duration;
    private ActionResult _actionResult;
    private CachedResult _hit;
    private bool _disabled;
    private NativeOutputCache(ControllerContext context, ActionDescriptor action, OutputCacheAttribute setting)
    {
        _context = context; _lifetime = NativeRequestLifetime.For(context.HttpContext);
        _core = ((INativeRoutingContext)context.HttpContext).CoreContext;
        _store = _core.RequestServices.GetRequiredService<IOutputCacheStore>();
        _logger = _core.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Mvc.OutputCache");
        _tempData = context.Controller.TempData; _tempVersion = _tempData.NativeMutationVersion;
        _replacementVersion = context.Controller.NativeTempDataVersion;
        _headers = _core.Response.Headers.ToDictionary(h => h.Key, h => h.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
        _duration = TimeSpan.FromSeconds(setting.Duration);
        var actionIdentity = context.Controller.GetType().AssemblyQualifiedName + ":" + action.UniqueId;
        var scope = _lifetime.OutputCacheOptions.ApplicationTag;
        _tags = new[] { scope, scope + ":action:" + Hash(actionIdentity) };
        var parts = new List<string> { "mvc-output-v1", scope, _lifetime.ChildCacheNamespace, actionIdentity,
            _core.Request.Method, _core.Request.Scheme, _core.Request.Host.Value, _core.Request.PathBase.Value, _core.Request.Path.Value,
            setting.Duration.ToString(System.Globalization.CultureInfo.InvariantCulture), setting.VaryByParam };
        foreach (var route in context.RouteData.Values.OrderBy(p => p.Key, StringComparer.Ordinal))
        { parts.Add(route.Key); parts.Add(route.Value?.GetType().AssemblyQualifiedName); parts.Add(Convert.ToString(route.Value, System.Globalization.CultureInfo.InvariantCulture)); }
        string query = _core.Request.QueryString.Value ?? "";
        if (setting.VaryByParam == "*") parts.Add(query);
        else if (!String.Equals(setting.VaryByParam.Trim(), "none", StringComparison.OrdinalIgnoreCase))
        {
            var names = setting.VaryByParam.Split(';').Select(n => n.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            // Select names case-insensitively, retain raw spelling/encoding/order
            // and every duplicate. Missing and empty selected values stay distinct.
            foreach (var pair in query.TrimStart('?').Split('&'))
                if (names.Contains(WebUtility.UrlDecode(pair.Split('=', 2)[0]))) parts.Add(pair);
        }
        var canonical = JsonSerializer.Serialize(parts);
        if (canonical.Length > 32768) { _disabled = true; _key = ""; }
        else _key = "mvc-output-v1:" + Hash(canonical);
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static void ObserveFailure(ControllerContext context)
    {
        if (context?.HttpContext is NativeChildContext) return; // Preserve U47's child/parent distinction.
        var lifetime = context?.HttpContext == null ? null : NativeRequestLifetime.For(context.HttpContext);
        if (lifetime != null) lifetime.OutputCacheFailed = true;
    }
    internal static void ObserveViewTempData(ViewContext context, TempDataDictionary value, int version)
    {
        if (context?.HttpContext == null || context.HttpContext is NativeChildContext) return;
        var lifetime = NativeRequestLifetime.For(context.HttpContext);
        if (lifetime != null && (version != 0 || context.GetType() != typeof(ViewContext) || !ReferenceEquals(value, context.Controller?.TempData)))
            lifetime.OutputCacheFailed = true;
    }
    private static void ValidateSettings(OutputCacheAttribute setting)
    {
        if (setting.NoStore || setting.Location == OutputCacheLocation.None) return;
        if (setting.GetType() != typeof(OutputCacheAttribute) || setting.Location != OutputCacheLocation.Server ||
            !String.IsNullOrEmpty(setting.CacheProfile) || !String.IsNullOrEmpty(setting.SqlDependency) ||
            !String.IsNullOrEmpty(setting.VaryByCustom) || !String.IsNullOrEmpty(setting.VaryByHeader) ||
            !String.IsNullOrEmpty(setting.VaryByContentEncoding))
            throw new PlatformNotSupportedException("Native top-level output caching requires explicit Server location and query-only variation; broader settings need a separate contract.");
        if (setting.Duration <= 0 || String.IsNullOrWhiteSpace(setting.VaryByParam))
            throw new InvalidOperationException("Output caching requires positive Duration and nonempty VaryByParam.");
    }
    internal static NativeOutputCache Prepare(ControllerContext context, IList<IActionFilter> filters, ActionDescriptor action)
    {
        var settings = filters.OfType<OutputCacheAttribute>().ToArray();
        if (settings.Length == 0) return null;
        if (settings.Length != 1) throw new PlatformNotSupportedException("Use one effective top-level output cache attribute.");
        var setting = settings[0]; ValidateSettings(setting);
        var lifetime = NativeRequestLifetime.For(context.HttpContext)
            ?? throw new PlatformNotSupportedException("Top-level caching requires the native awaited MVC application.");
        var core = ((INativeRoutingContext)context.HttpContext).CoreContext;
        if (!core.Response.Headers.CacheControl.ToString().Split(',').Any(v => String.Equals(v.Trim(), "no-store", StringComparison.OrdinalIgnoreCase)))
            core.Response.Headers.Append("Cache-Control", "no-store");
        if (setting.NoStore || setting.Location == OutputCacheLocation.None) return null;
        if (lifetime.OutputCacheOptions == null)
            throw new PlatformNotSupportedException("Register NativeOutputCacheOptions and native AddOutputCache before using Server output caching.");
        var cache = new NativeOutputCache(context, action, setting);
        lifetime.OutputCache = cache;
        return cache;
    }
    internal static void ValidateResultFilter(ControllerContext context, OutputCacheAttribute setting)
    {
        ValidateSettings(setting);
        if (NativeRequestLifetime.For(context.HttpContext) == null || context.Controller is not Controller controller || controller.ActionInvoker is not NativeAsyncActionInvoker)
            throw new PlatformNotSupportedException("Top-level output caching requires the owned native action invoker.");
    }
    private void Check() { _lifetime.CheckPublication(); _lifetime.ValidateOutputCacheOwner(); _ = ((INativeRoutingContext)_context.HttpContext).CoreContext; }
    private bool Eligible()
    {
        var user = _core.User;
        return !_disabled && !_lifetime.OutputCacheFailed && _core.Request.Method == "GET" &&
            !(user?.Identities.Any(i => i.IsAuthenticated || !String.IsNullOrEmpty(i.Name) || i.Claims.Any()) == true) &&
            !_core.Request.Headers.ContainsKey("Authorization") && !_core.Request.Headers.ContainsKey("Cookie") &&
            !_core.Request.Headers.ContainsKey("Range") && !_core.Request.Headers.Keys.Any(k => k.StartsWith("If-", StringComparison.OrdinalIgnoreCase)) &&
            _lifetime.Session == null && ReferenceEquals(_tempData, _context.Controller.TempData) && _tempData.Count == 0 &&
            _tempVersion == 1 && _tempData.NativeMutationVersion == _tempVersion && _replacementVersion == 0 &&
            _context.Controller.NativeTempDataVersion == _replacementVersion && !_core.Response.Headers.ContainsKey("Set-Cookie");
    }
    private bool HeadersUnchanged(bool prepared)
    {
        bool IsPrepared(string key) => prepared && (String.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase) || String.Equals(key, "Content-Length", StringComparison.OrdinalIgnoreCase));
        var current = _core.Response.Headers.Where(h => !IsPrepared(h.Key)).ToArray();
        var original = _headers.Where(h => !IsPrepared(h.Key)).ToArray();
        return current.Length == original.Length && current.All(h => _headers.TryGetValue(h.Key, out var values) && h.Value.SequenceEqual(values));
    }
    private bool EmptyResponse() => _context.HttpContext.Response is NativeBufferedResponse response && !response.IsBinary && response.BufferedLength == 0 && response.StatusCode == 200 && ReferenceEquals(response.ContentEncoding, Encoding.UTF8) && response.ContentType == "text/html" && response.Location == null && !response.TrySkipIisCustomErrors && response.DownloadDisposition == null;
    internal async Task<ActionResult> LookupAsync()
    {
        Check();
        if (!Eligible() || !EmptyResponse() || !HeadersUnchanged(false)) { _disabled = true; return null; }
        byte[] data;
        try { data = await _store.GetAsync(_key, _lifetime.RequestAborted); }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { _logger.LogWarning(error, "Output cache lookup failed; executing MVC."); data = null; }
        Check();
        if (!Eligible() || !EmptyResponse() || !HeadersUnchanged(false)) { _disabled = true; return null; }
        if (data == null || data.Length > 524288) return null;
        try
        {
            var entry = JsonSerializer.Deserialize<Entry>(data);
            if (entry?.Version != 1 || entry.Body == null || entry.Body.Length > 65536 || !ValidType(entry.ContentType)) return null;
            return _hit = new CachedResult(entry);
        }
        catch (JsonException error) { _logger.LogWarning(error, "Ignoring an invalid MVC cache representation."); return null; }
    }
    internal void AfterAction(ActionResult result) => _actionResult = result;
    internal bool IsHit(ActionResult result) => _hit != null && ReferenceEquals(result, _hit);
    internal void ValidateHit()
    {
        Check();
        // Challenge callbacks still run. A replacement result takes the ordinary
        // MVC filter path; a surviving marker must retain its stateless envelope.
        if (!Eligible() || !EmptyResponse() || !HeadersUnchanged(false))
            throw new InvalidOperationException("Authentication challenge changed a cached response envelope without replacing its result.");
        _hit.AllowExecution = true;
    }
    internal void BeforeResult(ActionResult result)
    {
        if (_hit != null || !ReferenceEquals(result, _actionResult) || !EmptyResponse() || !HeadersUnchanged(false)) _disabled = true;
    }
    internal async Task PublishAsync(NativeBufferedResponse response, Action validate)
    {
        Check(); validate();
        if (_hit != null || !Eligible() || !HeadersUnchanged(true) || response.IsBinary || response.StatusCode != 200 ||
            !ReferenceEquals(response.ContentEncoding, Encoding.UTF8) || response.Location != null || response.DownloadDisposition != null ||
            response.TrySkipIisCustomErrors || !ValidType(response.ContentType)) return;
        var entry = new Entry { Version = 1, ContentType = response.ContentType, Body = Encoding.UTF8.GetString(response.GetBytes()) };
        var data = JsonSerializer.SerializeToUtf8Bytes(entry);
        if (data.Length > 524288) return;
        try { await _store.SetAsync(_key, data, _tags, _duration, _lifetime.RequestAborted); }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { _logger.LogWarning(error, "Output cache publication failed; serving the completed MVC response."); }
        Check(); validate();
    }
    private static bool ValidType(string type) => type != null && type.Length <= 256 &&
        MediaTypeHeaderValue.TryParse(type, out var parsed) && parsed.MediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) && parsed.Parameters.Count == 0;
    private sealed class Entry { public int Version { get; set; } public string ContentType { get; set; } public string Body { get; set; } }
    private sealed class CachedResult(Entry entry) : ActionResult
    {
        internal bool AllowExecution;
        public override void ExecuteResult(ControllerContext context)
        {
            if (!AllowExecution) throw new PlatformNotSupportedException("A challenge wrapper cannot replay a complete cached MVC result through ordinary result filters.");
            AllowExecution = false;
            context.HttpContext.Response.ContentType = entry.ContentType; context.HttpContext.Response.Write(entry.Body);
        }
    }
}
