using System;
using System.Collections;
using System.IO;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using MvcNet10Host;

namespace AspNetWebStack.Native;

// Original Html.Action owns route/value-provider/parent metadata. This seam
// supplies nested synchronous MVC execution without emulating Server.Execute.
internal static class NativeChildActions
{
    // Observe before original filters receive mutable exception contexts. A
    // handled child error may still render successfully, but cannot populate cache.
    internal static void ObserveFailure(ControllerContext context)
    {
        if (context?.HttpContext is NativeChildContext child) child.InvalidateCacheWrites();
    }
    internal static bool IsOwned(ControllerContext context)
    {
        if (context.HttpContext is not NativeChildContext child) return false;
        child.Check(); return true;
    }
    internal static void CheckInitialized(ControllerBase controller, RequestContext request)
    {
        if (request.HttpContext is not NativeChildContext child) return;
        child.Check();
        if (controller is not Controller mvc || mvc.ActionInvoker is not ChildInvoker ||
            !ReferenceEquals(controller.ControllerContext.HttpContext, child) ||
            !ReferenceEquals(controller.ControllerContext.RouteData, request.RouteData) || !controller.ControllerContext.IsChildAction)
            throw new PlatformNotSupportedException("Child initialization must retain the owned context and synchronous MVC invoker.");
    }
    internal static void Execute(RequestContext request, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(writer);
        var lifetime = NativeRequestLifetime.For(request.HttpContext)
            ?? throw new PlatformNotSupportedException("Child actions require the native application request lifetime.");
        lifetime.CheckPublication();
        lifetime.OutputCacheFailed = true; // Parent hits must never hide child authorization.
        if (OutputCacheAttribute.IsChildActionCacheActive(new ControllerContext { HttpContext = request.HttpContext }))
            throw new InvalidOperationException("Native cached fragments must be leaf child actions; nested child authorization cannot run on an ancestor cache hit.");
        var factory = lifetime.ChildFactory ?? throw new InvalidOperationException("No owned child controller factory.");
        if (++lifetime.ChildDepth > 8) { lifetime.ChildDepth--; throw new InvalidOperationException("Child action nesting exceeds eight levels."); }
        try
        {
            using var child = new NativeChildContext(request.HttpContext, lifetime);
            var nested = new RequestContext(child, request.RouteData);
            var controller = factory.CreateController(nested, request.RouteData.GetRequiredString("controller"))
                ?? throw new InvalidOperationException("The child controller factory returned null.");
            try
            {
                if (controller is not Controller mvc || controller is AsyncController)
                    throw new PlatformNotSupportedException("Child actions require an ordinary synchronous MVC controller.");
                var invoker = mvc.ActionInvoker;
                if (invoker.GetType() != typeof(ControllerActionInvoker) && invoker.GetType() != typeof(System.Web.Mvc.Async.AsyncControllerActionInvoker) && invoker.GetType() != typeof(NativeAsyncActionInvoker))
                    throw new PlatformNotSupportedException("Child actions require the original MVC action invoker; custom invokers need an explicit synchronous child contract.");
                mvc.ActionInvoker = new ChildInvoker(((ControllerActionInvoker)invoker).DescriptorCache);
                controller.Execute(nested);
            }
            finally { factory.ReleaseController(controller); }
            child.Check();
            int status = child.Buffer.StatusCode;
            if (status < 200 || status > 599) throw new InvalidOperationException("Unsupported child response status.");
            string text = child.Buffer.ContentEncoding.GetString(child.Buffer.GetBytes());
            // Child metadata cannot replace the parent content type/headers. A
            // failed child status still reaches outer challenge/failure handling.
            if (status != 200) request.HttpContext.Response.StatusCode = status;
            writer.Write(text);
            lifetime.CheckPublication();
            // Capture stores characters before child encoding conversion. Only
            // the unchanged native UTF8 encoding can replay those characters.
            // Inspect after all filters and release; other encodings stay uncached.
            if (status == 200 && ReferenceEquals(child.Buffer.ContentEncoding, System.Text.Encoding.UTF8)) child.StageCacheWrites();
        }
        finally { lifetime.ChildDepth--; }
    }

    private sealed class ChildInvoker : ControllerActionInvoker
    {
        internal ChildInvoker(ControllerDescriptorCache descriptors) { DescriptorCache = descriptors; }
        protected override ActionDescriptor FindAction(ControllerContext context, ControllerDescriptor descriptor, string name)
        {
            var action = base.FindAction(context, descriptor, name);
            if (action is System.Web.Mvc.Async.AsyncActionDescriptor)
                throw new PlatformNotSupportedException("Html.Action and RenderAction require synchronous child actions.");
            if (action is ReflectedActionDescriptor reflected && (typeof(Task).IsAssignableFrom(reflected.MethodInfo.ReturnType) ||
                reflected.MethodInfo.ReturnType == typeof(ValueTask) ||
                (reflected.MethodInfo.ReturnType.IsGenericType && reflected.MethodInfo.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>))))
                throw new PlatformNotSupportedException("Html.Action and RenderAction require synchronous child actions.");
            return action;
        }
        protected override void InvokeActionResult(ControllerContext context, ActionResult result)
        {
            if (result is FileResult) throw new PlatformNotSupportedException("Child actions render bounded text; file results require a top-level request.");
            base.InvokeActionResult(context, result);
        }
    }
}

internal sealed class NativeChildContext : HttpContextBase, INativeRoutingContext, IDisposable
{
    private readonly HttpContextBase _parent;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private bool _completed;
    private bool _cacheFailed;
    internal bool CanCache => !_cacheFailed;
    internal NativeRequestLifetime NativeLifetime { get; }
    internal BufferedResponse Buffer { get; }
    internal readonly System.Collections.Generic.List<Action> CacheWrites = new();
    internal Action ClearCacheCallback;
    internal void InvalidateCacheWrites() { _cacheFailed = true; CacheWrites.Clear(); }
    internal void StageCacheWrites()
    {
        if (!_cacheFailed)
        {
            if (_parent is NativeChildContext child) child.CacheWrites.AddRange(CacheWrites);
            else NativeLifetime.ChildCacheWrites.AddRange(CacheWrites);
        }
        CacheWrites.Clear();
    }
    internal HttpContextBase Root => _parent is NativeChildContext child ? child.Root : _parent;
    internal NativeChildContext(HttpContextBase parent, NativeRequestLifetime lifetime)
    {
        _parent = parent; NativeLifetime = lifetime;
        Buffer = new BufferedResponse(lifetime.RequestAborted, CheckOwner);
    }
    private void CheckOwner()
    {
        NativeLifetime.CheckIdentity(); ObjectDisposedException.ThrowIf(_completed, this);
        if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Child actions require sequential synchronous ownership.");
    }
    internal void Check() { CheckOwner(); NativeLifetime.CheckPublication(); }
    Microsoft.AspNetCore.Http.HttpContext INativeRoutingContext.CoreContext { get { Check(); return ((INativeRoutingContext)_parent).CoreContext; } }
    public override HttpRequestBase Request { get { Check(); return _parent.Request; } }
    public override HttpResponseBase Response { get { Check(); return Buffer; } }
    public override IDictionary Items { get { Check(); return _parent.Items; } }
    public override IPrincipal User { get { Check(); return _parent.User; } set => throw new PlatformNotSupportedException("Native authentication owns the principal."); }
    public override HttpSessionStateBase Session { get { Check(); return _parent.Session; } }
    public override bool IsCustomErrorEnabled { get { Check(); return _parent.IsCustomErrorEnabled; } }
    public void Dispose() { ClearCacheCallback?.Invoke(); CacheWrites.Clear(); Buffer.Complete(); _completed = true; }
}
