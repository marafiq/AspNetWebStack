using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Mvc;
using System.Web.Mvc.Async;

namespace AspNetWebStack.Native;

// One action interval, independent of the public signature. Cancellation requests
// cooperation; actual Task/legacy work and cancellation dispatch remain owned.
internal sealed class NativeActionOperation
{
    private readonly object _control = new();
    private readonly NativeRequestLifetime _lifetime;
    private readonly CancellationTokenSource _deadline = new(), _application = new();
    // Only the internal supervisor consumes this signal. Completing it outside
    // _control permits the no-cancellation close to settle synchronously.
    private readonly TaskCompletionSource<bool> _wake = new();
    private readonly Task _supervisor;
    private Task _dispatch;
    private CancellationTokenRegistration _deadlineRegistration, _abortRegistration;
    private bool _closed, _timedOut, _aborted;

    private NativeActionOperation(ControllerContext context, int timeout)
    {
        _lifetime = NativeRequestLifetime.For(context.HttpContext);
        _supervisor = SuperviseAsync();
        _deadlineRegistration = _deadline.Token.UnsafeRegister(_ => Signal(true), null);
        _abortRegistration = _lifetime.RequestAborted.UnsafeRegister(_ => Signal(false), null);
        if (timeout == 0) _deadline.Cancel();
        else if (timeout > 0) _deadline.CancelAfter(timeout);
    }
    private void Signal(bool deadline)
    {
        lock (_control)
        {
            if (_closed) return;
            if (deadline) { _timedOut = true; _lifetime.MarkTimedOut(); }
            else _aborted = true;
        }
        _wake.TrySetResult(true);
    }
    private async Task SuperviseAsync()
    {
        if (await _wake.Task.ConfigureAwait(false))
        {
            // Retain and join the FIRST dispatch task. A subsequent CancelAsync
            // after cancellation is requested would not join these callbacks.
            _dispatch = _application.CancelAsync();
            await _dispatch.ConfigureAwait(false);
        }
    }
    private bool CancellationRequested
    {
        get { lock (_control) return _timedOut || _aborted || _deadline.IsCancellationRequested || _lifetime.RequestAborted.IsCancellationRequested; }
    }
    private void Inject(ParameterInfo[] infos, object[] values)
    {
        for (int i = 0; i < infos.Length; i++)
            if (infos[i].ParameterType == typeof(CancellationToken) && default(CancellationToken).Equals(values[i]))
            { values[i] = _application.Token; break; }
    }
    private async Task<NativeActionOutcome> CloseAsync(object value, ExceptionDispatchInfo failure, bool primaryFault, Exception secondary = null)
    {
        bool cancel;
        lock (_control)
        {
            // The deadline transition never takes the legacy Sync gate. Sampling
            // its source also accounts for a timer callback waiting to be scheduled.
            if (_deadline.IsCancellationRequested) { _timedOut = true; _lifetime.MarkTimedOut(); }
            _aborted |= _lifetime.RequestAborted.IsCancellationRequested;
            _closed = true;
            _deadline.CancelAfter(Timeout.Infinite);
            cancel = _timedOut || _aborted;
        }
        _wake.TrySetResult(cancel);
        Exception cleanup = secondary;
        try { await _deadlineRegistration.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { cleanup = Combine(cleanup, error); }
        try { await _abortRegistration.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { cleanup = Combine(cleanup, error); }
        try { await _supervisor.ConfigureAwait(false); }
        catch (Exception error) { cleanup = Combine(cleanup, error); }
        _deadline.Dispose(); _application.Dispose();
        // Request abort also remains an independent host publication veto later.
        if (!primaryFault)
        {
            if (_aborted || _lifetime.RequestAborted.IsCancellationRequested)
                failure = ExceptionDispatchInfo.Capture(new OperationCanceledException(_lifetime.RequestAborted));
            else if (_timedOut) failure = ExceptionDispatchInfo.Capture(new TimeoutException("The MVC action exceeded its cooperative timeout."));
        }
        if (cleanup != null)
        {
            if (failure == null) failure = ExceptionDispatchInfo.Capture(cleanup);
            else { try { failure.SourceException.Data["NativeActionCleanupFailure"] = cleanup; } catch { } }
        }
        return new NativeActionOutcome(value, failure);
    }
    private static Exception Combine(Exception first, Exception second) => first == null ? second : new AggregateException(first, second);

    internal static IAsyncResult BeginTask(ControllerContext context, AsyncManager manager, ParameterInfo[] infos, object[] values,
        Func<Task> invoke, Func<Task, object> extract, object tag, AsyncCallback callback, object state)
    {
        var operation = new NativeActionOperation(context, manager.Timeout);
        operation.Inject(infos, values);
        return NativeActionAsyncResult.Begin(operation.RunTaskAsync(invoke, extract), tag, callback, state);
    }
    private async Task<NativeActionOutcome> RunTaskAsync(Func<Task> invoke, Func<Task, object> extract)
    {
        Task task = null; object value = null; ExceptionDispatchInfo failure = null; bool primary = false;
        try
        {
            if (!CancellationRequested)
            {
                task = invoke() ?? throw new InvalidOperationException("The MVC Task action returned null.");
                await task.ConfigureAwait(false); // Never cancel the ownership wait.
                value = extract(task);
            }
        }
        catch (Exception error)
        {
            failure = ExceptionDispatchInfo.Capture(error);
            primary = task?.IsFaulted == true || error is not OperationCanceledException;
        }
        return await CloseAsync(value, failure, primary).ConfigureAwait(false);
    }
    internal static IAsyncResult BeginLegacy(ControllerContext context, AsyncManager manager, ParameterInfo[] infos, object[] values,
        Action initial, Func<object> complete, object tag, AsyncCallback callback, object state)
    {
        var owner = manager.NativeOperation ?? new NativeLegacyAsyncOperation(manager);
        owner.Claim();
        var operation = new NativeActionOperation(context, manager.Timeout);
        operation.Inject(infos, values);
        owner.Start(manager, initial, operation.CancellationRequested);
        return NativeActionAsyncResult.Begin(operation.RunLegacyAsync(owner, complete), tag, callback, state);
    }
    private async Task<NativeActionOutcome> RunLegacyAsync(NativeLegacyAsyncOperation owner, Func<object> complete)
    {
        await owner.Handoff.ConfigureAwait(false);
        object value = null; var failure = owner.Failure;
        try
        {
            if (failure == null && !CancellationRequested) value = owner.RunCompletion(complete);
        }
        catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); }
        var outcome = await CloseAsync(value, failure, failure != null && failure.SourceException is not OperationCanceledException, owner.Secondary).ConfigureAwait(false);
        owner.Clear();
        return outcome;
    }
}

internal sealed class NativeActionOutcome
{
    private readonly object _value;
    private readonly ExceptionDispatchInfo _failure;
    internal NativeActionOutcome(object value, ExceptionDispatchInfo failure) { _value = value; _failure = failure; }
    internal object Get() { _failure?.Throw(); return _value; }
}

// TaskToAsyncResult owns APM timing/state. MVC additionally requires descriptor
// identity, one End, and rejecting an incomplete End instead of blocking.
internal sealed class NativeActionAsyncResult : IAsyncResult
{
    private IAsyncResult _inner;
    private readonly object _tag;
    private int _consumed;
    private NativeActionAsyncResult(object tag) { _tag = tag; }
    internal static IAsyncResult Begin(Task<NativeActionOutcome> task, object tag, AsyncCallback callback, object state)
    {
        var result = new NativeActionAsyncResult(tag);
        result._inner = TaskToAsyncResult.Begin(task, inner => { result._inner = inner; callback?.Invoke(result); }, state);
        return result;
    }
    internal object End(object tag)
    {
        if (!ReferenceEquals(_tag, tag)) throw new InvalidOperationException("The MVC action result belongs to another descriptor.");
        if (!IsCompleted) throw new InvalidOperationException("MVC ended an incomplete action operation.");
        if (Interlocked.Exchange(ref _consumed, 1) != 0) throw new InvalidOperationException("MVC action result already consumed.");
        return TaskToAsyncResult.End<NativeActionOutcome>(_inner).Get();
    }
    public object AsyncState => _inner.AsyncState;
    public WaitHandle AsyncWaitHandle => _inner.AsyncWaitHandle;
    public bool CompletedSynchronously => _inner.CompletedSynchronously;
    public bool IsCompleted => _inner.IsCompleted;
}
