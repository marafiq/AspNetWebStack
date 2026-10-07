using System;
using System.Web.Mvc;
using System.Web.Routing;
using System.Web.SessionState;
using System.Threading.Tasks;
using System.Web.Mvc.Async;
using System.Web.WebPages.Scope;

namespace MvcNet10Host;

// Controller ownership from original MvcHandler: create, execute, release in
// finally. The native awaited entry preserves the original public sync path.
internal static class ControllerExecution
{
    internal static void Execute(RequestContext context, IControllerFactory factory)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        string name = context.RouteData.GetRequiredString("controller");
        if (factory.GetControllerSessionBehavior(context, name) != SessionStateBehavior.Disabled)
            throw new PlatformNotSupportedException("This host supports only explicitly disabled session; session persistence has not been ported.");
        IController controller = factory.CreateController(context, name)
            ?? throw new InvalidOperationException("The controller factory returned null for " + name + ".");
        try
        {
            controller.Execute(context);
        }
        finally
        {
            // The factory owns cleanup. No second host-side Dispose is permitted.
            factory.ReleaseController(controller);
        }
    }
    internal static async Task ExecuteAsync(RequestContext context, IControllerFactory factory)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(factory);
        string name = context.RouteData.GetRequiredString("controller");
        var mode = factory.GetControllerSessionBehavior(context, name);
        if (mode != SessionStateBehavior.Disabled && (context.HttpContext.Session == null ||
            context.HttpContext.Session.IsReadOnly != (mode == SessionStateBehavior.ReadOnly)))
            throw new PlatformNotSupportedException("Configure native session for this controller session mode.");
        IController controller = factory.CreateController(context, name)
            ?? throw new InvalidOperationException("The controller factory returned null for " + name + ".");
        try
        {
            // Async Begin bypasses ControllerBase.Execute's transient scope.
            // Scope disposal precedes release, matching the synchronous path.
            using (ScopeStorage.CreateTransientScope())
            {
                if (controller is IAsyncController asynchronous)
                    await Task.Factory.FromAsync((callback, state) => asynchronous.BeginExecute(context, callback, state),
                        asynchronous.EndExecute, null).ConfigureAwait(false);
                else controller.Execute(context);
            }
        }
        finally { factory.ReleaseController(controller); }

    }

}
