using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Mvc;
using System.Web.Mvc.Async;

namespace AspNetWebStack.Native;

// Original MVC owns selection, binding, filters, results and exception flow.
// Native descriptors supply the shared Task/deadline policy and legacy ownership gate.
internal sealed class NativeAsyncActionInvoker : AsyncControllerActionInvoker
{
    // Attribute registration and every action request share one application's
    // descriptor cache; mutable original StandardRouteMethods never cross catalogs.
    private NativeLegacyAsyncOperation _legacy;
    internal NativeAsyncActionInvoker(ControllerDescriptorCache descriptors) { DescriptorCache = descriptors; }

    protected internal override IAsyncResult BeginInvokeActionMethodWithFilters(ControllerContext context, IList<IActionFilter> filters,
        ActionDescriptor action, IDictionary<string, object> parameters, AsyncCallback callback, object state)
    {
        var pending = new TaskCompletionSource<ActionExecutedContext>(state, TaskCreationOptions.RunContinuationsAsynchronously);
        // Preserve APM state/callback without blocking an incomplete provider task.
        _ = CompleteAsync();
        return pending.Task;
        async Task CompleteAsync()
        {
            try
            {
                var cache = NativeOutputCache.Prepare(context, filters, action);
                var hit = cache == null ? null : await cache.LookupAsync();
                ActionExecutedContext result;
                if (hit != null) result = new ActionExecutedContext(context, action, true, null) { Result = hit };
                else result = await Task.Factory.FromAsync(
                    (cb, st) => BeginOriginalActionMethodWithFilters(context, filters, action, parameters, cb, st),
                    EndOriginalActionMethodWithFilters, null);
                cache?.AfterAction(result.Result);
                pending.TrySetResult(result);
            }
            // FromAsync preserves the original OCE object; a new TrySetCanceled
            // would discard it before exception filters. Preserve every error.
            catch (Exception error) { NativeOutputCache.ObserveFailure(context); pending.TrySetException(error); }
            callback?.Invoke(pending.Task);
        }
    }
    private IAsyncResult BeginOriginalActionMethodWithFilters(ControllerContext context, IList<IActionFilter> filters,
        ActionDescriptor action, IDictionary<string, object> parameters, AsyncCallback callback, object state)
    {
        if (action is not ReflectedAsyncActionDescriptor legacy)
            return base.BeginInvokeActionMethodWithFilters(context, filters, action, parameters, callback, state);
        NativeLegacyAsyncOperation.Validate(context, legacy);
        // Original Begin can synchronously unwind OnActionExecuted when binding
        // or descriptor Begin fails. That entire recursion is trusted MVC work.
        var owner = _legacy = new NativeLegacyAsyncOperation(((IAsyncManagerContainer)context.Controller).AsyncManager);
        try { return owner.RunBeginPipeline(() => base.BeginInvokeActionMethodWithFilters(context, filters, action, parameters, callback, state)); }
        finally { if (!owner.Started) owner.Clear(); } // Short circuit / pre-action failure: no operation will perform cleanup.
    }
    private ActionExecutedContext EndOriginalActionMethodWithFilters(IAsyncResult result)
    {
        // FromAsync may consume this original End inline or on its callback.
        // Original OnActionExecuted runs here, before the outer action End.
        var legacy = _legacy;
        return legacy == null ? base.EndInvokeActionMethodWithFilters(result)
            : legacy.RunPipeline(() => base.EndInvokeActionMethodWithFilters(result));
    }
    protected internal override ActionExecutedContext EndInvokeActionMethodWithFilters(IAsyncResult result)
    {
        var task = (Task<ActionExecutedContext>)result;
        if (!task.IsCompleted) throw new InvalidOperationException("MVC ended an incomplete output-cache continuation.");
        return task.GetAwaiter().GetResult();
    }
    protected override ResultExecutedContext InvokeActionResultWithFilters(ControllerContext context, IList<IResultFilter> filters, ActionResult result)
    {
        var cache = NativeRequestLifetime.For(context.HttpContext)?.OutputCache;
        if (cache?.IsHit(result) == true)
        {
            cache.ValidateHit();
            InvokeActionResult(context, result);
            return new ResultExecutedContext(context, result, false, null);
        }
        cache?.BeforeResult(result);
        return base.InvokeActionResultWithFilters(context, filters, result);
    }

    protected override void InvokeActionResult(ControllerContext context, ActionResult result)
    {
        var lifetime = NativeRequestLifetime.For(context.HttpContext);
        try { base.InvokeActionResult(context, result); }
        catch (Exception error)
        {
            if (result is FileResult) lifetime?.MarkFileFailure(error);
            throw; // Original result/exception filters still observe the error.
        }
    }

    protected internal override IAsyncResult BeginInvokeActionMethod(ControllerContext context, ActionDescriptor action,
        IDictionary<string, object> parameters, AsyncCallback callback, object state)
    {
        if (action is not ReflectedAsyncActionDescriptor legacy)
            return base.BeginInvokeActionMethod(context, action, parameters, callback, state);
        NativeLegacyAsyncOperation.Validate(context, legacy);
        // Install before Begin: a completed native Task/APM bridge may callback inline.
        _legacy ??= new NativeLegacyAsyncOperation(((IAsyncManagerContainer)context.Controller).AsyncManager);
        try
        {
            // Refresh before parameter extraction too: its synchronous error
            // filters must see the context established by OnActionExecuting.
            _legacy.CaptureActionOwner();
            return base.BeginInvokeActionMethod(context, action, parameters, callback, state);
        }
        catch { _legacy.Clear(); throw; }
    }

    public override bool EndInvokeAction(IAsyncResult result)
    {
        try { return _legacy == null ? base.EndInvokeAction(result) : _legacy.RunPipeline(() => base.EndInvokeAction(result)); }
        finally { _legacy?.Clear(); _legacy = null; }
    }

    protected override ActionDescriptor FindAction(ControllerContext context, ControllerDescriptor descriptor, string name)
    {
        var action = base.FindAction(context, descriptor, name);
        if (action is ReflectedAsyncActionDescriptor legacy) NativeLegacyAsyncOperation.Validate(context, legacy);
        return action;
    }
}
