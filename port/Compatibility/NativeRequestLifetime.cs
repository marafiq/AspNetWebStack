using System;
using System.Threading;
using System.Web;
using Microsoft.AspNetCore.Http;

namespace AspNetWebStack.Native;

// Opt-in lifetime for sequential awaited MVC execution. Native HttpContextAccessor
// owns request-flow identity; this shared state invalidates retained wrappers at
// completion. It does not make HttpContext safe for parallel/background use.
internal sealed class NativeRequestLifetime : IDisposable
{
    private readonly Microsoft.AspNetCore.Http.HttpContext _core;
    private readonly IHttpContextAccessor _accessor;
    private readonly IServiceProvider _services;
    private readonly NativeSessionOwnership _sessionOwner;
    private int _completed, _timedOut;
    private System.Runtime.ExceptionServices.ExceptionDispatchInfo _fileFailure;
    internal CancellationToken RequestAborted { get; }
    internal string JsonAntiforgeryHeader { get; }
    internal bool SessionEnabled { get; }
    internal NativeSessionState Session { get; set; }
    internal System.Web.Mvc.IControllerFactory ChildFactory { get; set; }
    internal int ChildDepth { get; set; }
    internal NativeOutputCacheOptions OutputCacheOptions { get; set; }
    internal NativeOutputCache OutputCache { get; set; }
    internal bool OutputCacheFailed { get; set; }
    internal Action ValidateOutputCacheOwner { get; set; }
    internal string ChildCacheNamespace { get; set; }
    internal readonly System.Collections.Generic.List<Action> ChildCacheWrites = new();
    internal void PublishChildCache()
    {
        CheckPublication();
        foreach (var publish in ChildCacheWrites) { CheckPublication(); publish(); }
        ChildCacheWrites.Clear();
    }
    internal NativeRequestLifetime(Microsoft.AspNetCore.Http.HttpContext core, IHttpContextAccessor accessor, string jsonAntiforgeryHeader = null, bool sessionEnabled = false)
    {
        JsonAntiforgeryHeader = jsonAntiforgeryHeader; SessionEnabled = sessionEnabled;
        _sessionOwner = new NativeSessionOwnership(core, sessionEnabled);
        _core = core; _accessor = accessor; _services = core.RequestServices;
        RequestAborted = core.RequestAborted; CheckIdentity();
    }
    internal static NativeRequestLifetime For(HttpContextBase context) => context switch
    {
        NativeQueryContext query => query.NativeLifetime,
        NativeFormContext form => form.NativeLifetime,
        NativeJsonContext json => json.NativeLifetime,
        NativeChildContext child => child.NativeLifetime,
        _ => null
    };
    internal void CheckIdentity()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _completed) != 0, this);
        if (!ReferenceEquals(_accessor.HttpContext, _core) || !ReferenceEquals(_services, _core.RequestServices) || RequestAborted != _core.RequestAborted)
            throw new InvalidOperationException("The native MVC request lifetime changed.");
    }
    internal void MarkTimedOut() => Interlocked.Exchange(ref _timedOut, 1);
    internal void MarkFileFailure(Exception error) => System.Threading.Interlocked.CompareExchange(ref _fileFailure, System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error), null);
    internal void CheckPublication()
    {
        CheckIdentity(); RequestAborted.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _timedOut) != 0) throw new TimeoutException("The MVC action exceeded its cooperative timeout.");
        _fileFailure?.Throw();
        _sessionOwner.Check(); Session?.ValidateOwner();
    }
    public void Dispose() => Interlocked.Exchange(ref _completed, 1);
}
