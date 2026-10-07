using System;
using System.Linq;
using System.Security.Claims;
using System.Runtime.Caching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Web.Mvc;
using System.Web.Routing;
using MvcNet10Host;

namespace AspNetWebStack.Native;

// The original filter owns ordering, key variation and callback semantics. This
// seam owns native eligibility, bounded capture and deferred publication only.
internal static class NativeChildCache
{
    internal static bool OwnsCapture(ControllerContext context) =>
        context.HttpContext is NativeChildContext child && child.ClearCacheCallback != null;
    private static ILogger Logger(ControllerContext context) =>
        ((INativeRoutingContext)context.HttpContext).CoreContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Mvc.ChildCache");
    internal static string Get(ControllerContext context, ObjectCache cache, string key)
    {
        try { return cache.Get(key) as string; }
        catch (Exception error) { Logger(context).LogWarning(error, "Child cache lookup failed; executing the action."); return null; }
    }
    internal static bool IsEligible(ActionExecutingContext context)
    {
        var child = context.HttpContext as NativeChildContext
            ?? throw new PlatformNotSupportedException("Child caching requires the owned native child context.");
        child.Check();
        var user = child.User;
        bool privatePrincipal = user?.Identity?.IsAuthenticated == true || !String.IsNullOrEmpty(user?.Identity?.Name) ||
            (user is ClaimsPrincipal claims && claims.Identities.Any(i => i.IsAuthenticated || i.Claims.Any()));
        return child.CanCache && !privatePrincipal && child.Request.HttpMethod == "GET" && child.NativeLifetime.Session == null &&
            context.ParentActionViewContext.GetType() == typeof(ViewContext) &&
            context.Controller.TempData.Count == 0 && context.ActionParameters.Values.All(IsScalar);
    }
    private static bool IsScalar(object value)
    {
        if (value == null) return true;
        var type = value.GetType();
        return type.IsEnum || type.IsPrimitive || value is string || value is decimal || value is Guid || value is DateTime || value is DateTimeOffset || value is TimeSpan;
    }
    internal static string ScopeKey(ActionExecutingContext context, string key) =>
        ((NativeChildContext)context.HttpContext).NativeLifetime.ChildCacheNamespace + ":" + key;
    internal static Capture Begin(ActionExecutingContext context) => new(context);
    internal static void RegisterCleanup(ControllerContext context, object key, Action<bool> callback)
    {
        var child = (NativeChildContext)context.HttpContext;
        var items = child.Items;
        child.ClearCacheCallback = () => { if (ReferenceEquals(items[key], callback)) items.Remove(key); };
    }
    internal sealed class Capture
    {
        private readonly NativeChildContext _child;
        private readonly BufferedResponse _capture;
        private readonly TempDataDictionary _tempData;
        private readonly int _version, _parentVersion;
        private readonly ViewContext _parent;
        private readonly ILogger _logger;
        internal Capture(ActionExecutingContext context)
        {
            _child = (NativeChildContext)context.HttpContext;
            _parent = context.ParentActionViewContext; _parentVersion = _parent.NativeTempDataVersion; _logger = Logger(context);
            _tempData = context.Controller.TempData; _version = _tempData.NativeMutationVersion;
            _capture = _child.Buffer.BeginCapture();
        }
        internal void Finish(bool wasException, Action<string> publish)
        {
            string text = _child.Buffer.EndCapture(_capture);
            _child.Check();
            _child.Buffer.Write(text);
            if (!wasException && _child.CanCache && _child.Buffer.StatusCode == 200)
                _child.CacheWrites.Add(() => {
                    // Controller/root disposal has finished. Do not dereference a
                    // completed child context; shared TempData itself is retained.
                    if (_parent.NativeTempDataVersion == _parentVersion && ReferenceEquals(_parent.TempData, _tempData) && _tempData.Count == 0 && _tempData.NativeMutationVersion == _version)
                    {
                        try { publish(text); }
                        catch (Exception error) { _logger.LogWarning(error, "Child cache publication failed; serving the completed response."); }
                    }
                });
        }
    }
}
