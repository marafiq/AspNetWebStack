using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Mvc;
using System.Web.Mvc.Async;

namespace AspNetWebStack.Native;

// Serializes admitted request work. Handoff is a declaration, never a cancellation proxy.
internal sealed class NativeLegacyAsyncOperation
{
    private readonly object _serial = new();
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ExecutionContext _owner;
    private bool _declared, _returned, _closed, _started, _clearPending;
    private int _depth, _trustedThread, _pipelineDepth;
    private ExceptionDispatchInfo _failure;
    private Exception _secondary;
    internal static void ValidateController(Controller controller)
    {
        if (controller is not AsyncController) return;
        var type = controller.GetType();
        foreach (var method in new[] { "BeginExecute", "BeginExecuteCore", "EndExecute", "EndExecuteCore", "get_DisableAsyncSupport" })
            if (type.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).DeclaringType != typeof(Controller))
                throw new PlatformNotSupportedException("Custom legacy controller APM execution is outside the native profile.");
        var map = type.GetInterfaceMap(typeof(IAsyncController));
        foreach (var method in map.TargetMethods)
            if (method.DeclaringType != typeof(Controller))
                throw new PlatformNotSupportedException("Custom legacy controller APM execution is outside the native profile.");
    }

    internal static void CheckInitialized(Controller controller, System.Web.Routing.RequestContext request)
    {
        if (controller is not AsyncController || NativeRequestLifetime.For(request.HttpContext) == null) return;
        if (controller.ActionInvoker is not NativeAsyncActionInvoker ||
            !ReferenceEquals(controller.ControllerContext.Controller, controller) ||
            !ReferenceEquals(controller.ControllerContext.HttpContext, request.HttpContext) ||
            !ReferenceEquals(controller.ControllerContext.RouteData, request.RouteData) || controller.ControllerContext.IsChildAction)
            throw new PlatformNotSupportedException("Legacy initialization must retain the owned controller, context, route and native async invoker.");
    }

    internal static void Validate(ControllerContext context, ReflectedAsyncActionDescriptor action)
    {
        var controller = context.Controller as AsyncController;
        var manager = (context.Controller as IAsyncManagerContainer)?.AsyncManager;
        if (context.IsChildAction || controller == null || manager == null || manager.GetType() != typeof(AsyncManager) ||
            !ReferenceEquals(manager, controller.AsyncManager) || !manager.NativeOrdinaryContext ||
            action.GetType() != typeof(ReflectedAsyncActionDescriptor) || action.AsyncMethodInfo.ReturnType != typeof(void) ||
            action.AsyncMethodInfo.IsDefined(typeof(AsyncStateMachineAttribute), false) ||
            action.CompletedMethodInfo.IsDefined(typeof(AsyncStateMachineAttribute), false) ||
            typeof(Task).IsAssignableFrom(action.CompletedMethodInfo.ReturnType) ||
            action.CompletedMethodInfo.ReturnType == typeof(ValueTask) ||
            (action.CompletedMethodInfo.ReturnType.IsGenericType && action.CompletedMethodInfo.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>)))
            throw new PlatformNotSupportedException("Native legacy actions require a top-level ordinary AsyncController manager, void FooAsync and synchronous FooCompleted; custom contexts and async-void are outside this profile.");
        // Controller's protected APM overrides execute before action selection;
        // NativeMvcApplication rejects these at activation, before BeginExecute.
    }

    internal NativeLegacyAsyncOperation(AsyncManager manager)
    {
        _owner = ExecutionContext.Capture() ?? throw new InvalidOperationException("Legacy MVC requires flowing ExecutionContext.");
        if (manager.NativeOperation != null) throw new InvalidOperationException("The legacy AsyncManager already has an execution owner.");
        manager.NativeOperation = this;
        manager.OutstandingOperations.NativeOperation = this;
    }

    internal bool Started { get { lock (_serial) return _started; } }
    internal void CaptureActionOwner()
    {
        lock (_serial)
            _owner = ExecutionContext.Capture() ?? throw new InvalidOperationException("Legacy MVC requires flowing ExecutionContext.");
    }
    internal void Claim()
    {
        lock (_serial)
        {
            if (_started) throw new InvalidOperationException("The legacy AsyncManager already has an execution owner.");
            // Action filters may change CultureInfo or AsyncLocal values. The
            // cooperative action owner is the descriptor-entry context.
            CaptureActionOwner();
            _started = true;
        }
    }
    internal void Start(AsyncManager manager, Action initial, bool skip)
    {
        lock (_serial)
        {
            _depth++;
            try
            {
                if (skip) _declared = true;
                else
                {
                    manager.OutstandingOperations.Increment();
                    try { initial(); }
                    catch (Exception error) { _failure = ExceptionDispatchInfo.Capture(error); }
                    finally
                    {
                        // Only the framework's sentinel is ours to balance.
                        try { manager.OutstandingOperations.Decrement(); }
                        catch (Exception error) { Record(error); }
                    }
                }
            }
            finally { _depth--; _returned = true; Signal(); }
        }
    }
    internal Task Handoff => _ready.Task;
    internal ExceptionDispatchInfo Failure => _failure;
    internal Exception Secondary => _secondary;
    private void Record(Exception error)
    {
        if (_failure == null) _failure = ExceptionDispatchInfo.Capture(error);
        else if (!ReferenceEquals(_failure.SourceException, error)) _secondary = error;
    }
    private void Signal()
    {
        if (_returned && _declared && _depth == 0) { _closed = true; _ready.TrySetResult(true); }
    }
    internal void Notify(Action notification, bool declaresHandoff)
    {
        lock (_serial)
        {
            CheckAdmission();
            _depth++;
            try
            {
                if (declaresHandoff) _declared = true;
                ExecutionContext.Run(_owner, _ => notification(), null);
            }
            catch (Exception error) { Record(error); throw; }
            finally { _depth--; Signal(); }
        }
    }
    private void CheckAdmission()
    {
        if ((_closed || (_declared && _depth == 0)) && _trustedThread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Legacy MVC completion was declared; Sync admission is closed.");
    }
    internal void Sync(Action action)
    {
        lock (_serial)
        {
            CheckAdmission();
            _depth++;
            try
            {
                // Trusted MVC filters may update ambient state within their
                // scope. Preserve that call's context; foreign callbacks use
                // the admitted action-entry owner captured before invocation.
                var owner = _trustedThread == Environment.CurrentManagedThreadId
                    ? ExecutionContext.Capture() ?? throw new InvalidOperationException("Legacy MVC requires flowing ExecutionContext.")
                    : _owner;
                ExecutionContext.Run(owner, _ => action(), null);
            }
            finally { _depth--; Signal(); }
        }
    }
    internal T RunCompletion<T>(Func<T> action)
    {
        T value = default;
        lock (_serial)
        {
            ExecutionContext.Run(_owner, _ => { value = RunPipeline(action); }, null);
        }
        return value;
    }
    // Original synchronous Begin unwind, method-filter End and outer End each own a trusted scope.
    internal T RunBeginPipeline<T>(Func<T> action) => RunPipeline(action, retainActionOwner: true);
    internal T RunPipeline<T>(Func<T> action) => RunPipeline(action, retainActionOwner: false);
    private T RunPipeline<T>(Func<T> action, bool retainActionOwner)
    {
        lock (_serial)
        {
            var priorOwner = _owner; var priorThread = _trustedThread;
            _owner = ExecutionContext.Capture() ?? throw new InvalidOperationException("Legacy completion requires flowing ExecutionContext.");
            _trustedThread = Environment.CurrentManagedThreadId;
            _pipelineDepth++;
            try { return action(); }
            finally
            {
                _trustedThread = priorThread;
                // Begin encloses Claim: keep its post-filter capture for delayed
                // callbacks/completion. Other trusted scopes restore their caller.
                if (!retainActionOwner || !_started) _owner = priorOwner;
                if (--_pipelineDepth == 0 && _clearPending) Clear();
            }
        }
    }
    internal void Clear()
    {
        lock (_serial)
        {
            // Reject foreign/late callers now, but admitted synchronous filter
            // unwind still needs its captured owner until the last scope exits.
            _closed = true; _clearPending = true;
            if (_pipelineDepth == 0) { _owner = null; _failure = null; _secondary = null; }
        }
    }
}
