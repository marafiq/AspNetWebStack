// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Net.Http;
using System.Web.Http.Properties;

namespace System.Web.Http.Routing
{
    /// <summary>
    /// A single route that is the composite of multiple "sub routes".  
    /// </summary>
    /// <remarks>
    /// Corresponds to the MVC implementation of attribute routing in System.Web.Mvc.Routing.RouteCollectionRoute.
    /// </remarks>
    internal class RouteCollectionRoute : IHttpRoute, IReadOnlyCollection<IHttpRoute>
    {
        // Key for accessing SubRoutes on a RouteData.
        // We expose this through the RouteData.Values instead of a derived class because 
        // RouteData can get wrapped in another type, but Values still gets persisted through the wrappers. 
        // Prefix with a \0 to protect against conflicts with user keys. 
        public const string SubRouteDataKey = "MS_SubRoutes";

        private IReadOnlyCollection<IHttpRoute> _subRoutes;

        private static readonly IDictionary<string, object> _empty = EmptyReadOnlyDictionary<string, object>.Value;
        
        public RouteCollectionRoute()
        {
        }

        // This will enumerate all controllers and action descriptors, which will run those 
        // Initialization hooks, which may try to initialize controller-specific config, which
        // may call back to the initialize hook. So guard against that reentrancy.
        private bool _beingInitialized;

        // Fast index over subroutes: method+leading-literal-prefix -> routes (preserves order)
        private Dictionary<string, List<IHttpRoute>> _methodPrefixIndex;
        private List<IHttpRoute> _parameterFirst;
        private bool _indexed;

        // deferred hook for initializing the sub routes. The composite route can be added during the middle of 
        // intializing, but then the actual sub routes can get populated after initialization has finished. 
        public void EnsureInitialized(Func<IReadOnlyCollection<IHttpRoute>> initializer)
        {
            if (_beingInitialized && _subRoutes == null)
            {
                // Avoid reentrant initialization
                return;
            }

            try
            {
                _beingInitialized = true;

                _subRoutes = initializer();
                Contract.Assert(_subRoutes != null);                
                _indexed = false; // will build on demand
            }
            finally
            {
                _beingInitialized = false;
            }
        }

        private IReadOnlyCollection<IHttpRoute> SubRoutes
        {
            get
            {
                // Caller should have already explicitly called EnsureInitialize. 
                // Avoid lazy initilization from within the route table because the route table
                // is shared resource and init can happen 
                if (_subRoutes == null)
                {
                    string msg = Error.Format(SRResources.Object_NotYetInitialized);
                    throw new InvalidOperationException(msg);
                }

                return _subRoutes;
            }
        }

        public string RouteTemplate
        {
            get { return String.Empty; }
        }

        public IDictionary<string, object> Defaults
        {
            get { return _empty; }
        }

        public IDictionary<string, object> Constraints
        {
            get { return _empty; }
        }

        public IDictionary<string, object> DataTokens
        {
            get { return null; }
        }

        public HttpMessageHandler Handler
        {
            get
            {
                return null;
            }
        }
                
        // Returns null if no match. 
        // Else, returns a composite route data that encapsulates the possible routes this may match against. 
        public IHttpRouteData GetRouteData(string virtualPathRoot, HttpRequestMessage request)
        {
            EnsureIndex();

            List<IHttpRouteData> matches = new List<IHttpRouteData>();

            // Try indexed candidates first (longest literal prefix with HTTP method)
            List<IHttpRoute> candidates = GetCandidatesByMethodAndPrefix(virtualPathRoot, request);
            if (candidates != null && candidates.Count > 0)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    IHttpRouteData match = candidates[i].GetRouteData(virtualPathRoot, request);
                    if (match != null)
                    {
                        matches.Add(match);
                    }
                }
            }

            // Fallback: scan remaining subroutes if nothing matched via index
            if (matches.Count == 0)
            {
                foreach (IHttpRoute route in SubRoutes)
                {
                    IHttpRouteData match = route.GetRouteData(virtualPathRoot, request);
                    if (match != null)
                    {
                        matches.Add(match);
                    }
                }
            }
            if (matches.Count == 0)
            {
                return null;  // no matches
            }

            return new RouteCollectionRouteData(this, matches.ToArray());
        }

        public IHttpVirtualPathData GetVirtualPath(HttpRequestMessage request, IDictionary<string, object> values)
        {
            // Use LinkGenerationRoute stubs to get placeholders for all the sub routes. 
            return null;
        }

        public int Count
        {
            get { return SubRoutes.Count; }
        }

        public IEnumerator<IHttpRoute> GetEnumerator()
        {
            return SubRoutes.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return SubRoutes.GetEnumerator();
        }

        private void EnsureIndex()
        {
            if (_indexed)
            {
                return;
            }

            _methodPrefixIndex = new Dictionary<string, List<IHttpRoute>>(StringComparer.OrdinalIgnoreCase);
            _parameterFirst = new List<IHttpRoute>();

            foreach (IHttpRoute route in SubRoutes)
            {
                string prefix = GetLeadingLiteralPrefix(route.RouteTemplate);
                IList<string> methods = GetAllowedHttpMethods(route);

                if (prefix.Length == 0)
                {
                    _parameterFirst.Add(route);
                }

                if (methods == null || methods.Count == 0)
                {
                    AddToMethodPrefixIndex("*", prefix, route);
                }
                else
                {
                    for (int i = 0; i < methods.Count; i++)
                    {
                        AddToMethodPrefixIndex(methods[i], prefix, route);
                    }
                }
            }

            _indexed = true;
        }

        private void AddToMethodPrefixIndex(string method, string prefix, IHttpRoute route)
        {
            string key = method + "|" + prefix;
            List<IHttpRoute> bucket;
            if (!_methodPrefixIndex.TryGetValue(key, out bucket))
            {
                bucket = new List<IHttpRoute>();
                _methodPrefixIndex[key] = bucket;
            }
            bucket.Add(route);
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

        private List<IHttpRoute> GetCandidatesByMethodAndPrefix(string virtualPathRoot, HttpRequestMessage request)
        {
            List<IHttpRoute> candidates = new List<IHttpRoute>();

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

                string mk = method + "|" + prefix;
                List<IHttpRoute> bucket;
                if (_methodPrefixIndex.TryGetValue(mk, out bucket))
                {
                    candidates.AddRange(bucket);
                }

                string ak = "*|" + prefix;
                if (_methodPrefixIndex.TryGetValue(ak, out bucket))
                {
                    candidates.AddRange(bucket);
                }

                if (candidates.Count > 0)
                {
                    break;
                }
            }

            // Always include parameter-first
            if (_parameterFirst.Count > 0)
            {
                candidates.AddRange(_parameterFirst);
            }

            return candidates;
        }

        // Represents a union of multiple IHttpRouteDatas. 
        private class RouteCollectionRouteData : IHttpRouteData
        {
            public RouteCollectionRouteData(IHttpRoute parent, IHttpRouteData[] subRouteDatas)
            {
                Route = parent;

                // Each sub route may have different values. Callers need to enumerate the subroutes 
                // and individually query each. 
                // Find sub-routes via the SubRouteDataKey; don't expose as a property since the RouteData 
                // can be wrapped in an outer type that doesn't propagate properties. 
                Values = new HttpRouteValueDictionary() { { SubRouteDataKey, subRouteDatas } };
            }

            public IHttpRoute Route { get; private set; }

            public IDictionary<string, object> Values { get; private set; }
        }        
    }
}