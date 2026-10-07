using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Web.WebPages.Scope;

namespace AspNetWebStack.Native
{
    // Configure once at host startup. Original ControllerBase owns the transient
    // push/pop; AsyncLocal keeps simultaneous native requests out of each other's scope.
    public sealed class NativeRequestScopeStorageProvider : IScopeStorageProvider
    {
        private readonly AsyncLocal<IDictionary<object, object>> _current = new();
        public IDictionary<object, object> GlobalScope { get; } =
            new ScopeStorageDictionary(null, new ConcurrentDictionary<object, object>(ScopeStorageComparer.Instance));
        public IDictionary<object, object> CurrentScope { get => _current.Value ?? GlobalScope; set => _current.Value = value; }
    }
}
