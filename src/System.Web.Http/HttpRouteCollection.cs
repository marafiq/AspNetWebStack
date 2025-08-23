// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Net.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Properties;
using System.Web.Http.Routing;

namespace System.Web.Http
{
    public class HttpRouteCollection : ICollection<IHttpRoute>, IDisposable
    {
        // Arbitrary base address for evaluating the root virtual path
        private static readonly Uri _referenceBaseAddress = new Uri("http://localhost");

        private readonly string _virtualPathRoot;
        private readonly List<IHttpRoute> _collection = new List<IHttpRoute>();
        private readonly IDictionary<string, IHttpRoute> _dictionary = new Dictionary<string, IHttpRoute>(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;

        // Fast lookup index: maps first literal segment -> ordered list of route indices
        private readonly Dictionary<string, List<int>> _firstSegmentIndex = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        // Routes whose first segment is parameter/catch-all or unknown
        private readonly List<int> _parameterFirstIndices = new List<int>();
        // Flag to indicate index must be rebuilt (after inserts/removes)
        private bool _indexDirty;
        // Extended fast index: maps "METHOD|seg1/seg2/..." (longest leading literal prefix) -> ordered list of route indices
        private readonly Dictionary<string, List<int>> _methodPrefixIndex = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpRouteCollection"/> class with a <see cref="M:VirtualPathRoot"/>
        /// value of "/".
        /// </summary>
        public HttpRouteCollection()
            : this("/")
        {
        }

        [SuppressMessage("Microsoft.Usage", "CA2234:PassSystemUriObjectsInsteadOfStrings", Justification = "Relative URIs are not URIs")]
        public HttpRouteCollection(string virtualPathRoot)
        {
            if (virtualPathRoot == null)
            {
                throw Error.ArgumentNull("virtualPathRoot");
            }

            // Validate virtual path
            Uri address = new Uri(_referenceBaseAddress, virtualPathRoot);
            _virtualPathRoot = "/" + address.GetComponents(UriComponents.Path, UriFormat.Unescaped);
        }

        public virtual string VirtualPathRoot
        {
            get { return _virtualPathRoot; }
        }

        public virtual int Count
        {
            get { return _collection.Count; }
        }

        public virtual bool IsReadOnly
        {
            get { return false; }
        }

        public virtual IHttpRoute this[int index]
        {
            get { return _collection[index]; }
        }

        public virtual IHttpRoute this[string name]
        {
            get { return _dictionary[name]; }
        }

        public virtual IHttpRouteData GetRouteData(HttpRequestMessage request)
        {
            if (request == null)
            {
                throw Error.ArgumentNull("request");
            }

            // Ensure index is up to date
            if (_indexDirty)
            {
                RebuildIndex();
            }

            string virtualPathRoot = GetVirtualPathRoot(request.GetRequestContext());

            // Compute first segment of the request relative to the virtual path root
            string firstRequestSegment = GetFirstRequestSegment(virtualPathRoot, request);
            if (firstRequestSegment == null)
            {
                return null;
            }

            // First try: longest-leading-literal-prefix index with HTTP method filtering
            List<int> candidateIndices = GetCandidatesByMethodAndPrefix(virtualPathRoot, request);

            // Fallback: first-segment index
            if (candidateIndices.Count == 0)
            {
                List<int> bucket;
                if (_firstSegmentIndex.TryGetValue(firstRequestSegment, out bucket))
                {
                    candidateIndices.AddRange(bucket);
                }
                // Always also try parameter-first routes (they can match any first segment)
                if (_parameterFirstIndices.Count > 0)
                {
                    candidateIndices.AddRange(_parameterFirstIndices);
                }
            }

            IHttpRouteData routeData;
            if (candidateIndices.Count > 0)
            {
                // De-duplicate and iterate in ascending order of insertion index to preserve semantics
                HashSet<int> seen = new HashSet<int>();
                List<int> ordered = new List<int>(candidateIndices.Count);
                for (int i = 0; i < candidateIndices.Count; i++)
                {
                    int idx = candidateIndices[i];
                    if (seen.Add(idx))
                    {
                        ordered.Add(idx);
                    }
                }
                ordered.Sort();

                for (int i = 0; i < ordered.Count; i++)
                {
                    int idx = ordered[i];
                    routeData = _collection[idx].GetRouteData(virtualPathRoot, request);
                    if (routeData != null)
                    {
                        return routeData;
                    }
                }

                // Fallback: if nothing matched, scan the remaining routes (unlikely)
                if (ordered.Count < _collection.Count)
                {
                    for (int i = 0; i < _collection.Count; i++)
                    {
                        if (!seen.Contains(i))
                        {
                            routeData = _collection[i].GetRouteData(virtualPathRoot, request);
                            if (routeData != null)
                            {
                                return routeData;
                            }
                        }
                    }
                }

                return null;
            }

            // No indexed candidates (e.g., empty table) - fall back to linear scan
            for (int i = 0; i < _collection.Count; i++)
            {
                routeData = _collection[i].GetRouteData(virtualPathRoot, request);
                if (routeData != null)
                {
                    return routeData;
                }
            }

            return null;
        }

        public virtual IHttpVirtualPathData GetVirtualPath(HttpRequestMessage request, string name, IDictionary<string, object> values)
        {
            if (request == null)
            {
                throw Error.ArgumentNull("request");
            }

            if (name == null)
            {
                throw Error.ArgumentNull("name");
            }

            IHttpRoute route;
            if (!_dictionary.TryGetValue(name, out route))
            {
                throw Error.Argument("name", SRResources.RouteCollection_NameNotFound, name);
            }
            IHttpVirtualPathData virtualPath = route.GetVirtualPath(request, values);
            if (virtualPath == null)
            {
                return null;
            }

            // Construct a new VirtualPathData with the resolved app path
            string virtualPathRoot = GetVirtualPathRoot(request.GetRequestContext());
            if (!virtualPathRoot.EndsWith("/", StringComparison.Ordinal))
            {
                virtualPathRoot += "/";
            }

            // Note: The virtual path root here always ends with a "/" and the
            // virtual path never starts with a "/" (that's how routes work).
            return new HttpVirtualPathData(virtualPath.Route, virtualPathRoot + virtualPath.VirtualPath);
        }

        // Returns the virtual path root on the request context if present
        // Otherwise, fall back on the virtual path root for the route collection
        private string GetVirtualPathRoot(HttpRequestContext requestContext)
        {
            if (requestContext != null)
            {
                return requestContext.VirtualPathRoot ?? String.Empty;
            }

            return _virtualPathRoot;
        }

        public IHttpRoute CreateRoute(string routeTemplate, object defaults, object constraints)
        {
            IDictionary<string, object> dataTokens = new Dictionary<string, object>();

            return CreateRoute(routeTemplate, new HttpRouteValueDictionary(defaults), new HttpRouteValueDictionary(constraints), dataTokens, handler: null);
        }

        public IHttpRoute CreateRoute(string routeTemplate, IDictionary<string, object> defaults, IDictionary<string, object> constraints, IDictionary<string, object> dataTokens)
        {
            return CreateRoute(routeTemplate, defaults, constraints, dataTokens, handler: null);
        }

        public virtual IHttpRoute CreateRoute(string routeTemplate, IDictionary<string, object> defaults, IDictionary<string, object> constraints, IDictionary<string, object> dataTokens, HttpMessageHandler handler)
        {
            HttpRouteValueDictionary routeDefaults = new HttpRouteValueDictionary(defaults);
            HttpRouteValueDictionary routeConstraints = new HttpRouteValueDictionary(constraints);
            HttpRouteValueDictionary routeDataTokens = new HttpRouteValueDictionary(dataTokens);

            foreach (var constraint in routeConstraints)
            {
                ValidateConstraint(routeTemplate, constraint.Key, constraint.Value);
            }

            return new HttpRoute(routeTemplate, routeDefaults, routeConstraints, routeDataTokens, handler);
        }

        /// <summary>
        /// Validates that a constraint is valid for an <see cref="IHttpRoute"/> created by a call
        /// to the <see cref="HttpRouteCollection.CreateRoute(string, IDictionary&lt;string, object&gt;, IDictionary&lt;string, object&gt;, IDictionary&lt;string, object&gt;, HttpMessageHandler)"/> method.
        /// </summary>
        /// <param name="routeTemplate">The route template.</param>
        /// <param name="name">The constraint name.</param>
        /// <param name="constraint">The constraint object.</param>
        /// <remarks>
        /// Implement this method when deriving from <see cref="HttpRouteCollection"/> to allow contraints of
        /// types other than <see cref="string"/> and <see cref="IHttpRouteConstraint"/>.
        /// </remarks>
        protected virtual void ValidateConstraint(string routeTemplate, string name, object constraint)
        {
            if (name == null)
            {
                throw Error.ArgumentNull("name");
            }

            if (constraint == null)
            {
                throw Error.ArgumentNull("constraint");
            }

            HttpRoute.ValidateConstraint(routeTemplate, name, constraint);
        }

        void ICollection<IHttpRoute>.Add(IHttpRoute route)
        {
            throw Error.NotSupported(SRResources.Route_AddRemoveWithNoKeyNotSupported, typeof(HttpRouteCollection).Name);
        }

        public virtual void Add(string name, IHttpRoute route)
        {
            if (name == null)
            {
                throw Error.ArgumentNull("name");
            }

            if (route == null)
            {
                throw Error.ArgumentNull("route");
            }

            _dictionary.Add(name, route);
            _collection.Add(route);
            IndexRouteAt(_collection.Count - 1, route);
        }

        public virtual void Clear()
        {
            _dictionary.Clear();
            _collection.Clear();
            _firstSegmentIndex.Clear();
            _parameterFirstIndices.Clear();
            _methodPrefixIndex.Clear();
            _indexDirty = false;
        }

        public virtual bool Contains(IHttpRoute item)
        {
            if (item == null)
            {
                throw Error.ArgumentNull("item");
            }

            return _collection.Contains(item);
        }

        public virtual bool ContainsKey(string name)
        {
            if (name == null)
            {
                throw Error.ArgumentNull("name");
            }

            return _dictionary.ContainsKey(name);
        }

        public virtual void CopyTo(IHttpRoute[] array, int arrayIndex)
        {
            _collection.CopyTo(array, arrayIndex);
        }

        public virtual void CopyTo(KeyValuePair<string, IHttpRoute>[] array, int arrayIndex)
        {
            _dictionary.CopyTo(array, arrayIndex);
        }

        public virtual void Insert(int index, string name, IHttpRoute value)
        {
            if (name == null)
            {
                throw Error.ArgumentNull("name");
            }

            if (value == null)
            {
                throw Error.ArgumentNull("value");
            }

            // Check that index is valid
            if (_collection[index] != null)
            {
                _dictionary.Add(name, value);
                _collection.Insert(index, value);
                // Rebuild index due to shifted positions
                _indexDirty = true;
                RebuildIndex();
            }
        }

        bool ICollection<IHttpRoute>.Remove(IHttpRoute route)
        {
            throw Error.NotSupported(SRResources.Route_AddRemoveWithNoKeyNotSupported, typeof(HttpRouteCollection).Name);
        }

        public virtual bool Remove(string name)
        {
            if (name == null)
            {
                throw Error.ArgumentNull("name");
            }

            IHttpRoute value;
            if (_dictionary.TryGetValue(name, out value))
            {
                bool dictionaryRemove = _dictionary.Remove(name);
                bool collectionRemove = _collection.Remove(value);
                Contract.Assert(dictionaryRemove == collectionRemove);
                // Rebuild index due to shifted positions
                _indexDirty = true;
                RebuildIndex();
                return dictionaryRemove;
            }

            return false;
        }

        public virtual IEnumerator<IHttpRoute> GetEnumerator()
        {
            return _collection.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return OnGetEnumerator();
        }

        protected virtual IEnumerator OnGetEnumerator()
        {
            return _collection.GetEnumerator();
        }

        public virtual bool TryGetValue(string name, out IHttpRoute route)
        {
            return _dictionary.TryGetValue(name, out route);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Creating a collection to avoid double-disposing any handlers that are shared between routes
                    HashSet<IDisposable> handlers = new HashSet<IDisposable>();
                    foreach (var route in this)
                    {
                        if (route.Handler != null)
                        {
                            handlers.Add(route.Handler);
                        }
                    }

                    foreach (var handler in handlers)
                    {
                        handler.Dispose();
                    }
                }

                _disposed = true;
            }
        }

        // Build or rebuild the first-segment index over the current route list
        private void RebuildIndex()
        {
            _firstSegmentIndex.Clear();
            _parameterFirstIndices.Clear();
            _methodPrefixIndex.Clear();

            for (int i = 0; i < _collection.Count; i++)
            {
                IndexRouteAt(i, _collection[i]);
            }

            _indexDirty = false;
        }

        // Index a single route at the specified position
        private void IndexRouteAt(int index, IHttpRoute route)
        {
            string key = GetFirstLiteralSegment(route.RouteTemplate);
            if (key == null)
            {
                _parameterFirstIndices.Add(index);
            }
            else
            {
                List<int> bucket;
                if (!_firstSegmentIndex.TryGetValue(key, out bucket))
                {
                    bucket = new List<int>();
                    _firstSegmentIndex[key] = bucket;
                }
                bucket.Add(index);
            }

            // Extended: index by longest leading literal prefix and HTTP method(s)
            string prefix = GetLeadingLiteralPrefix(route.RouteTemplate);
            IList<string> methods = GetAllowedHttpMethods(route);
            if (methods == null || methods.Count == 0)
            {
                // Any method
                AddToMethodPrefixIndex("*", prefix, index);
            }
            else
            {
                for (int i = 0; i < methods.Count; i++)
                {
                    AddToMethodPrefixIndex(methods[i], prefix, index);
                }
            }
        }

        private void AddToMethodPrefixIndex(string method, string prefix, int index)
        {
            string k = method + "|" + prefix;
            List<int> bucket;
            if (!_methodPrefixIndex.TryGetValue(k, out bucket))
            {
                bucket = new List<int>();
                _methodPrefixIndex[k] = bucket;
            }
            bucket.Add(index);
        }

        // Extract first literal segment from a route template; returns null if segment is parameter/catch-all
        private static string GetFirstLiteralSegment(string template)
        {
            if (String.IsNullOrEmpty(template))
            {
                return String.Empty; // root
            }

            int slash = template.IndexOf('/');
            string first = slash >= 0 ? template.Substring(0, slash) : template;
            if (first.Length == 0)
            {
                return String.Empty;
            }

            // Parameter or catch-all segments start with '{'
            if (first[0] == '{')
            {
                return null;
            }

            return first;
        }

        // Compute first path segment of the request relative to the given virtual path root. Returns null if outside root.
        private static string GetFirstRequestSegment(string virtualPathRoot, HttpRequestMessage request)
        {
            string requestPath = "/" + request.RequestUri.GetComponents(UriComponents.Path, UriFormat.Unescaped);

            // Fast path: exact case match first
            if (!requestPath.StartsWith(virtualPathRoot, StringComparison.Ordinal))
            {
                if (!requestPath.StartsWith(virtualPathRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }

            int start = virtualPathRoot.Length;
            if (requestPath.Length > start && requestPath[start] == '/')
            {
                start++;
            }

            if (start >= requestPath.Length)
            {
                return String.Empty;
            }

            int nextSlash = requestPath.IndexOf('/', start);
            if (nextSlash < 0)
            {
                return requestPath.Substring(start);
            }
            return requestPath.Substring(start, nextSlash - start);
        }

        // Get longest-prefix candidates for the request HTTP method and decreasing literal prefixes of the request path
        private List<int> GetCandidatesByMethodAndPrefix(string virtualPathRoot, HttpRequestMessage request)
        {
            List<int> candidates = new List<int>();

            string method = request.Method != null ? request.Method.Method : String.Empty;
            List<string> segments = GetRequestSegments(virtualPathRoot, request);
            if (segments == null)
            {
                return candidates;
            }

            // Try progressively shorter prefixes: seg1/seg2/... -> seg1/seg2 -> seg1 -> ""
            for (int len = segments.Count; len >= 0; len--)
            {
                string prefix = JoinSegments(segments, len);

                // method-specific bucket
                string mk = method + "|" + prefix;
                List<int> bucket;
                if (_methodPrefixIndex.TryGetValue(mk, out bucket))
                {
                    candidates.AddRange(bucket);
                }

                // any-method bucket
                string ak = "*|" + prefix;
                if (_methodPrefixIndex.TryGetValue(ak, out bucket))
                {
                    candidates.AddRange(bucket);
                }

                if (candidates.Count > 0)
                {
                    // Prefer the longest prefix that yields candidates
                    break;
                }
            }

            // Always include parameter-first (no literal prefix) routes as last resort
            if (_parameterFirstIndices.Count > 0)
            {
                candidates.AddRange(_parameterFirstIndices);
            }

            return candidates;
        }

        private static List<string> GetRequestSegments(string virtualPathRoot, HttpRequestMessage request)
        {
            string requestPath = "/" + request.RequestUri.GetComponents(UriComponents.Path, UriFormat.Unescaped);

            if (!requestPath.StartsWith(virtualPathRoot, StringComparison.Ordinal))
            {
                if (!requestPath.StartsWith(virtualPathRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }

            int start = virtualPathRoot.Length;
            if (requestPath.Length > start && requestPath[start] == '/')
            {
                start++;
            }

            if (start > requestPath.Length)
            {
                return new List<string>();
            }

            string rel = start < requestPath.Length ? requestPath.Substring(start) : String.Empty;
            if (String.IsNullOrEmpty(rel))
            {
                return new List<string>();
            }
            string[] parts = rel.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            return new List<string>(parts);
        }

        private static string JoinSegments(List<string> segments, int len)
        {
            if (len <= 0)
            {
                return String.Empty;
            }
            if (len > segments.Count)
            {
                len = segments.Count;
            }
            if (len == 1)
            {
                return segments[0];
            }
            return String.Join("/", segments.GetRange(0, len).ToArray());
        }

        private static string GetLeadingLiteralPrefix(string template)
        {
            if (String.IsNullOrEmpty(template))
            {
                return String.Empty;
            }
            string[] parts = template.Split('/');
            if (parts.Length == 0)
            {
                return String.Empty;
            }
            List<string> literals = new List<string>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (String.IsNullOrEmpty(p))
                {
                    continue;
                }
                // Stop on any dynamic token
                if (p.IndexOf('{') >= 0)
                {
                    break;
                }
                literals.Add(p);
            }
            if (literals.Count == 0)
            {
                return String.Empty;
            }
            return String.Join("/", literals.ToArray());
        }

        private static IList<string> GetAllowedHttpMethods(IHttpRoute route)
        {
            if (route == null || route.Constraints == null)
            {
                return null;
            }

            object constraint;
            if (!route.Constraints.TryGetValue("httpMethod", out constraint) || constraint == null)
            {
                return null;
            }

            HttpMethodConstraint methodConstraint = constraint as HttpMethodConstraint;
            if (methodConstraint == null || methodConstraint.AllowedMethods == null || methodConstraint.AllowedMethods.Count == 0)
            {
                return null;
            }

            List<string> methods = new List<string>(methodConstraint.AllowedMethods.Count);
            for (int i = 0; i < methodConstraint.AllowedMethods.Count; i++)
            {
                HttpMethod m = methodConstraint.AllowedMethods[i];
                methods.Add(m != null ? m.Method : String.Empty);
            }
            return methods;
        }
    }
}
