// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Web.Routing;

namespace System.Web.Mvc.Routing
{
    /// <summary>
    /// A single route that is the composite of multiple "sub routes".  
    /// </summary>
    /// <remarks>
    /// Corresponds to the Web API implementation of attribute routing in System.Web.Http.Routing.RouteCollectionRoute.
    /// </remarks>
    internal class RouteCollectionRoute : RouteBase, IReadOnlyCollection<RouteBase>
    {
        private readonly IReadOnlyCollection<RouteBase> _subRoutes;
        // Fast index over subroutes by leading literal prefix (seg1/seg2/...), preserves insertion order
        private readonly Dictionary<string, List<RouteBase>> _prefixIndex = new Dictionary<string, List<RouteBase>>(StringComparer.OrdinalIgnoreCase);
        private readonly List<RouteBase> _parameterFirst = new List<RouteBase>();

        public RouteCollectionRoute(IReadOnlyCollection<RouteBase> subRoutes)
        {
            if (subRoutes == null)
            {
                throw new ArgumentNullException("subRoutes");
            }

            _subRoutes = subRoutes;

            // Build index once since subroutes are immutable for this instance
            BuildIndex();
        }

        public override RouteData GetRouteData(HttpContextBase httpContext)
        {
            List<RouteData> matches = new List<RouteData>();

            // Try indexed candidates first (longest leading literal prefix)
            List<RouteBase> candidates = GetCandidatesByPrefix(httpContext);
            if (candidates != null && candidates.Count > 0)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    RouteData match = candidates[i].GetRouteData(httpContext);
                    if (match != null)
                    {
                        matches.Add(match);
                    }
                }
            }

            // Fallback: scan remaining subroutes if needed
            if (matches.Count == 0)
            {
                foreach (RouteBase route in _subRoutes)
                {
                    var match = route.GetRouteData(httpContext);
                    if (match != null)
                    {
                        matches.Add(match);
                    }
                }
            }

            return CreateDirectRouteMatch(this, matches);
        }

        public override VirtualPathData GetVirtualPath(RequestContext requestContext, RouteValueDictionary values)
        {
            // Link generation is not supported via the RouteCollectionRoute - see LinkGenerationRoute.
            return null;
        }

        public int Count
        {
            get { return _subRoutes.Count; }
        }

        public IEnumerator<RouteBase> GetEnumerator()
        {
            return _subRoutes.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _subRoutes.GetEnumerator();
        }

        private void BuildIndex()
        {
            foreach (RouteBase route in _subRoutes)
            {
                string prefix = GetLeadingLiteralPrefix(route);
                if (prefix.Length == 0)
                {
                    _parameterFirst.Add(route);
                    continue;
                }

                List<RouteBase> bucket;
                if (!_prefixIndex.TryGetValue(prefix, out bucket))
                {
                    bucket = new List<RouteBase>();
                    _prefixIndex[prefix] = bucket;
                }
                bucket.Add(route);
            }
        }

        private static string GetLeadingLiteralPrefix(RouteBase route)
        {
            Route r = route as Route;
            if (r == null || String.IsNullOrEmpty(r.Url))
            {
                return String.Empty;
            }

            string[] parts = r.Url.Split('/');
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

        private List<RouteBase> GetCandidatesByPrefix(HttpContextBase httpContext)
        {
            List<RouteBase> candidates = new List<RouteBase>();

            // Build request path relative to application root (trim leading "~/")
            string appRel = httpContext.Request.AppRelativeCurrentExecutionFilePath ?? "~/";
            string path = appRel.StartsWith("~/", StringComparison.Ordinal) ? appRel.Substring(2) : appRel;
            string pathInfo = httpContext.Request.PathInfo;
            if (!String.IsNullOrEmpty(pathInfo))
            {
                if (path.Length > 0 && !path.EndsWith("/", StringComparison.Ordinal))
                {
                    path += "/";
                }
                path += pathInfo.TrimStart('/');
            }

            if (path.Length == 0)
            {
                // root
                List<RouteBase> bucket;
                if (_prefixIndex.TryGetValue(String.Empty, out bucket))
                {
                    candidates.AddRange(bucket);
                }
                candidates.AddRange(_parameterFirst);
                return candidates;
            }

            string[] parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            for (int len = parts.Length; len >= 0; len--)
            {
                string prefix = Join(parts, len);
                List<RouteBase> bucket;
                if (_prefixIndex.TryGetValue(prefix, out bucket))
                {
                    candidates.AddRange(bucket);
                }
                if (candidates.Count > 0)
                {
                    break;
                }
            }

            if (_parameterFirst.Count > 0)
            {
                candidates.AddRange(_parameterFirst);
            }

            return candidates;
        }

        private static string Join(string[] parts, int len)
        {
            if (len <= 0)
            {
                return String.Empty;
            }
            if (len == 1)
            {
                return parts[0];
            }
            if (len > parts.Length)
            {
                len = parts.Length;
            }
            return String.Join("/", parts, 0, len);
        }

        public static RouteData CreateDirectRouteMatch(RouteBase route, List<RouteData> matches)
        {
            if (matches.Count == 0)
            {
                return null;
            }
            else
            {
                var routeData = new RouteData();
                routeData.Route = route;
                routeData.RouteHandler = new MvcRouteHandler();
                routeData.SetDirectRouteMatches(matches);

                // At a few points in the code (MvcRouteHandler, MvcHandler) we need to look up the controller
                // by name. For the purposes of error handling/debugging, it's helpful if we can have a name
                // in this code to pass through.
                //
                // Inside the DefaultControllerFactory we'll double check the route data and throw if we have
                // multiple controller matches, but for now let's just use the controller of the first match.
                ControllerDescriptor controllerDescriptor = matches[0].GetTargetControllerDescriptor();
                if (controllerDescriptor != null)
                {
                    routeData.Values[RouteDataTokenKeys.Controller] = controllerDescriptor.ControllerName;
                }

                return routeData;
            }
        }
    }
}