// Legacy-facing shapes for the native endpoint -> original MVC hand-off.
// Native conventional routes are configured at startup and frozen when bound.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace System.Web.Routing
{
    public class RouteValueDictionary : IDictionary<string, object>
    {
        private readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);
        public RouteValueDictionary() { }
        public RouteValueDictionary(object values)
        {
            foreach (var pair in new Microsoft.AspNetCore.Routing.RouteValueDictionary(values)) _values.Add(pair.Key, pair.Value);
        }
        public RouteValueDictionary(IDictionary<string, object> dictionary)
        {
            if (dictionary == null) throw new ArgumentNullException(nameof(dictionary));
            foreach (var pair in dictionary) _values.Add(pair.Key, pair.Value);
        }
        public object this[string key] { get => _values.TryGetValue(key, out var value) ? value : null; set => _values[key] = value; }
        public int Count => _values.Count;
        public Dictionary<string, object>.KeyCollection Keys => _values.Keys;
        public Dictionary<string, object>.ValueCollection Values => _values.Values;
        ICollection<string> IDictionary<string, object>.Keys => Keys;
        ICollection<object> IDictionary<string, object>.Values => Values;
        bool ICollection<KeyValuePair<string, object>>.IsReadOnly => false;
        public void Add(string key, object value) => _values.Add(key, value);
        public void Clear() => _values.Clear();
        public bool ContainsKey(string key) => _values.ContainsKey(key);
        public bool ContainsValue(object value) => _values.ContainsValue(value);
        public bool Remove(string key) => _values.Remove(key);
        public bool TryGetValue(string key, out object value) => _values.TryGetValue(key, out value);
        public Dictionary<string, object>.Enumerator GetEnumerator() => _values.GetEnumerator();
        IEnumerator<KeyValuePair<string, object>> IEnumerable<KeyValuePair<string, object>>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        void ICollection<KeyValuePair<string, object>>.Add(KeyValuePair<string, object> item) => ((ICollection<KeyValuePair<string, object>>)_values).Add(item);
        bool ICollection<KeyValuePair<string, object>>.Contains(KeyValuePair<string, object> item) => ((ICollection<KeyValuePair<string, object>>)_values).Contains(item);
        void ICollection<KeyValuePair<string, object>>.CopyTo(KeyValuePair<string, object>[] array, int index) => ((ICollection<KeyValuePair<string, object>>)_values).CopyTo(array, index);
        bool ICollection<KeyValuePair<string, object>>.Remove(KeyValuePair<string, object> item) => ((ICollection<KeyValuePair<string, object>>)_values).Remove(item);
    }
    public class RouteData
    {
        public RouteData() { }
        public RouteData(RouteBase route, IRouteHandler routeHandler) { Route = route; RouteHandler = routeHandler; }
        public RouteValueDictionary Values { get; } = new();
        public RouteValueDictionary DataTokens { get; } = new();
        public RouteBase Route { get; set; }
        public IRouteHandler RouteHandler { get; set; }
        public string GetRequiredString(string valueName) => Values[valueName] is string s && s.Length > 0 ? s : throw new InvalidOperationException("Missing route value: " + valueName);
    }
    public class RequestContext
    {
        public RequestContext() { }
        public RequestContext(HttpContextBase httpContext, RouteData routeData)
        {
            HttpContext = httpContext ?? throw new ArgumentNullException(nameof(httpContext));
            RouteData = routeData ?? throw new ArgumentNullException(nameof(routeData));
        }
        public virtual HttpContextBase HttpContext { get; set; }
        public virtual RouteData RouteData { get; set; }
    }
    public abstract class RouteBase
    {
        protected RouteBase() { }
        public bool RouteExistingFiles { get; set; }
        public abstract RouteData GetRouteData(HttpContextBase httpContext);
        public abstract VirtualPathData GetVirtualPath(RequestContext requestContext, RouteValueDictionary values);
    }
    public class Route : RouteBase
    {
        public Route(string url, IRouteHandler routeHandler) : this(url, null, null, null, routeHandler) { }
        public Route(string url, RouteValueDictionary defaults, IRouteHandler routeHandler) : this(url, defaults, null, null, routeHandler) { }
        public Route(string url, RouteValueDictionary defaults, RouteValueDictionary constraints, IRouteHandler routeHandler) : this(url, defaults, constraints, null, routeHandler) { }
        public Route(string url, RouteValueDictionary defaults, RouteValueDictionary constraints, RouteValueDictionary dataTokens, IRouteHandler routeHandler)
        { Url = url; Defaults = defaults; Constraints = constraints; DataTokens = dataTokens; RouteHandler = routeHandler; }
        public string Url { get; set; }
        public RouteValueDictionary Defaults { get; set; }
        public RouteValueDictionary Constraints { get; set; }
        public RouteValueDictionary DataTokens { get; set; }
        public IRouteHandler RouteHandler { get; set; }
        internal NativeRouteBinding NativeBinding { get; set; }
        public override RouteData GetRouteData(HttpContextBase httpContext) => (NativeBinding ?? throw Unavailable()).GetRouteData(httpContext, this);
        public override VirtualPathData GetVirtualPath(RequestContext requestContext, RouteValueDictionary values) => (NativeBinding ?? throw Unavailable()).GetVirtualPath(requestContext, values, this, applicationPath: false);
        protected virtual bool ProcessConstraint(HttpContextBase httpContext, object constraint, string parameterName, RouteValueDictionary values, RouteDirection routeDirection) => throw Unavailable();
        internal static PlatformNotSupportedException Unavailable() => new("Native conventional routing does not support custom callbacks, physical-file routing, rebinding or changes after startup.");
    }
    public class RouteCollection : Collection<RouteBase>
    {
        private readonly Dictionary<string, RouteBase> _names = new(StringComparer.OrdinalIgnoreCase);
        private readonly System.Threading.ReaderWriterLockSlim _gate = new(System.Threading.LockRecursionPolicy.SupportsRecursion);
        internal NativeRouteBinding NativeBinding { get; set; }
        internal AspNetWebStack.Native.NativeControllerCatalog NativeCatalog { get; set; }
        internal string NameFor(RouteBase route) => _names.FirstOrDefault(pair => ReferenceEquals(pair.Value, route)).Key;
        public RouteCollection() { }
        public RouteCollection(System.Web.Hosting.VirtualPathProvider virtualPathProvider) => throw Route.Unavailable();
        public bool AppendTrailingSlash { get; set; }
        public bool LowercaseUrls { get; set; }
        public bool RouteExistingFiles { get; set; }
        public RouteBase this[string name] => name != null && _names.TryGetValue(name, out var route) ? route : null;
        public void Add(string name, RouteBase item)
        {
            using (GetWriteLock())
            {
                Mutable(); ArgumentNullException.ThrowIfNull(item);
                if (!String.IsNullOrEmpty(name) && _names.ContainsKey(name)) throw new ArgumentException("A route with this name already exists.", nameof(name));
                base.InsertItem(Count, item);
                if (!String.IsNullOrEmpty(name)) _names.Add(name, item);
            }
        }
        public IDisposable GetReadLock() { _gate.EnterReadLock(); return new Unlock(_gate.ExitReadLock); }
        public IDisposable GetWriteLock() { _gate.EnterWriteLock(); return new Unlock(_gate.ExitWriteLock); }
        public RouteData GetRouteData(HttpContextBase httpContext)
        {
            if (NativeBinding != null) return NativeBinding.GetRouteData(httpContext);
            using (GetReadLock()) foreach (var route in this) { var data = route.GetRouteData(httpContext); if (data != null) return data; }
            return null;
        }
        public VirtualPathData GetVirtualPath(RequestContext requestContext, RouteValueDictionary values) => GetVirtualPath(requestContext, null, values);
        public VirtualPathData GetVirtualPath(RequestContext requestContext, string name, RouteValueDictionary values)
        {
            ArgumentNullException.ThrowIfNull(requestContext);
            NativeRouteBinding.RequireContext(requestContext.HttpContext);
            NativeBinding?.ValidateUnchanged();
            using (GetReadLock())
            {
                if (!String.IsNullOrEmpty(name) && this[name] == null) throw new ArgumentException("A route with the specified name was not found.", nameof(name));
                IEnumerable<RouteBase> candidates = String.IsNullOrEmpty(name) ? this : this[name] is RouteBase named ? new[] { named } : Array.Empty<RouteBase>();
                foreach (var item in candidates)
                {
                    if (item is System.Web.Mvc.Routing.RouteCollectionRoute) continue;
                    if (item is not Route route || route.NativeBinding == null) throw Route.Unavailable();
                    var path = route.NativeBinding.GetVirtualPath(requestContext, values, route, applicationPath: true, options: this);
                    if (path != null) return path;
                }
            }
            return null;
        }
        private void Mutable() { if (NativeBinding != null) throw Route.Unavailable(); }
        private void Forget(RouteBase route) { foreach (var name in _names.Where(pair => ReferenceEquals(pair.Value, route)).Select(pair => pair.Key).ToArray()) _names.Remove(name); }
        private sealed class Unlock : IDisposable
        {
            private Action _release;
            internal Unlock(Action release) { _release = release; }
            public void Dispose() { var release = _release; _release = null; release?.Invoke(); }
        }
        public void Ignore(string url) => throw Route.Unavailable();
        public void Ignore(string url, object constraints) => throw Route.Unavailable();
        public Route MapPageRoute(string routeName, string routeUrl, string physicalFile) => throw Route.Unavailable();
        public Route MapPageRoute(string routeName, string routeUrl, string physicalFile, bool checkPhysicalUrlAccess) => throw Route.Unavailable();
        public Route MapPageRoute(string routeName, string routeUrl, string physicalFile, bool checkPhysicalUrlAccess, RouteValueDictionary defaults) => throw Route.Unavailable();
        public Route MapPageRoute(string routeName, string routeUrl, string physicalFile, bool checkPhysicalUrlAccess, RouteValueDictionary defaults, RouteValueDictionary constraints) => throw Route.Unavailable();
        public Route MapPageRoute(string routeName, string routeUrl, string physicalFile, bool checkPhysicalUrlAccess, RouteValueDictionary defaults, RouteValueDictionary constraints, RouteValueDictionary dataTokens) => throw Route.Unavailable();
        protected override void ClearItems() { using (GetWriteLock()) { Mutable(); base.ClearItems(); _names.Clear(); } }
        protected override void InsertItem(int index, RouteBase item) { using (GetWriteLock()) { Mutable(); ArgumentNullException.ThrowIfNull(item); base.InsertItem(index, item); } }
        protected override void RemoveItem(int index) { using (GetWriteLock()) { Mutable(); var prior = this[index]; base.RemoveItem(index); Forget(prior); } }
        protected override void SetItem(int index, RouteBase item) { using (GetWriteLock()) { Mutable(); ArgumentNullException.ThrowIfNull(item); var prior = this[index]; base.SetItem(index, item); Forget(prior); } }
    }
    public class VirtualPathData
    {
        public VirtualPathData(RouteBase route, string virtualPath) { Route = route; VirtualPath = virtualPath; }
        public RouteBase Route { get; set; }
        public string VirtualPath { get; set; }
        public RouteValueDictionary DataTokens { get; } = new();
    }
    public interface IRouteHandler { IHttpHandler GetHttpHandler(RequestContext requestContext); }
    public interface IRouteConstraint { bool Match(HttpContextBase httpContext, Route route, string parameterName, RouteValueDictionary values, RouteDirection routeDirection); }
    public enum RouteDirection { IncomingRequest, UrlGeneration }
    public class RouteTable { public static RouteCollection Routes { get; } = new(); }
    public class StopRoutingHandler : IRouteHandler
    {
        protected virtual IHttpHandler GetHttpHandler(RequestContext requestContext) => throw Route.Unavailable();
        IHttpHandler IRouteHandler.GetHttpHandler(RequestContext requestContext) => GetHttpHandler(requestContext);
    }
}
