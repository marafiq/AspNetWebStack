// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Web.Mvc.Properties;
using System.Web.Mvc.Routing;
using System.Web.Routing;
using System.Web.WebPages;
using System.Collections;
using System.Text;
using System.Linq;
using System.Globalization;

namespace System.Web.Mvc
{
    public static class RouteCollectionExtensions
    {
        private static Dictionary<string,RouteCollection> AreaRoutes = new Dictionary<string,RouteCollection>();
        private static bool UseStaticAreaFilteredRoutes = false;
        public static RouteCollection EnableStaticAreaFilteredRoutes(this RouteCollection routes){
            UseStaticAreaFilteredRoutes = true;
            //Group routes by area name and build the dictionary
            
            using (routes.GetReadLock())
            {
                HashTable<string> areas=new HashTable<string>();
                //get all unique areas
                foreach (RouteBase route in routes)
                {
                    string areaName = AreaHelpers.GetAreaName(route) ?? String.Empty;
                    
                    if (!string.IsNullOrEmpty(areaName))
                    {
                        areas.Add(areaName);
                    }
                }

                //Build dictionary for all area routes
                foreach (string area in areas)
                {
                    RouteCollection filteredRoutes = new RouteCollection()
                    {
                        AppendTrailingSlash = routes.AppendTrailingSlash,
                        LowercaseUrls = routes.LowercaseUrls,
                        RouteExistingFiles = routes.RouteExistingFiles
                    };
                    bool usingAreas=false;
                    foreach (RouteBase route in routes)
                    {
                        string thisAreaName = AreaHelpers.GetAreaName(route) ?? String.Empty;
                        usingAreas |= (thisAreaName.Length > 0);
                        if (!string.IsNullOrEmpty(thisAreaName, area))
                        {
                            filteredRoutes.Add(route);
                        }
                    }
                    //Only add to dictionary if there are routes for the area
                    if(usingAreas){
                        AreaRoutes.Add(area, filteredRoutes);
                    }
                }
            }
        }
        
        // This method returns a new RouteCollection containing only routes that matched a particular area.
        // The Boolean out parameter is just a flag specifying whether any registered routes were area-aware.
        private static RouteCollection FilterRouteCollectionByArea(RouteCollection routes, string areaName, out bool usingAreas)
        {
            if (areaName == null)
            {
                areaName = String.Empty;
            }

            usingAreas = false;

            
            
            if (UseStaticAreaFilteredRoutes) {
                usingAreas = AreaRoutes.ContainsKey(areaName);

                return usingAreas ? AreaRoutes[areaName] : routes;
            }

            // Ensure that we continue using the same settings as the previous route collection
            // if we are using areas and the route collection is exchanged
            RouteCollection filteredRoutes = new RouteCollection()
            {
                AppendTrailingSlash = routes.AppendTrailingSlash,
                LowercaseUrls = routes.LowercaseUrls,
                RouteExistingFiles = routes.RouteExistingFiles
            };
            using (routes.GetReadLock())
            {
                foreach (RouteBase route in routes)
                {
                    string thisAreaName = AreaHelpers.GetAreaName(route) ?? String.Empty;
                    usingAreas |= (thisAreaName.Length > 0);
                    if (String.Equals(thisAreaName, areaName, StringComparison.OrdinalIgnoreCase))
                    {
                        filteredRoutes.Add(route);
                    }
                }
            }

            // if areas are not in use, the filtered route collection might be incorrect
            return (usingAreas) ? filteredRoutes : routes;
        }

        public static VirtualPathData GetVirtualPathForArea(this RouteCollection routes, RequestContext requestContext, RouteValueDictionary values)
        {
            return GetVirtualPathForArea(routes, requestContext, null /* name */, values);
        }

        public static VirtualPathData GetVirtualPathForArea(this RouteCollection routes, RequestContext requestContext, string name, RouteValueDictionary values)
        {
            bool usingAreas; // don't care about this value
            return GetVirtualPathForArea(routes, requestContext, name, values, out usingAreas);
        }

        internal static VirtualPathData GetVirtualPathForArea(this RouteCollection routes, RequestContext requestContext, string name, RouteValueDictionary values, out bool usingAreas)
        {
            if (routes == null)
            {
                throw new ArgumentNullException("routes");
            }

            if (!String.IsNullOrEmpty(name))
            {
                // the route name is a stronger qualifier than the area name, so just pipe it through
                usingAreas = false;

                // Per-request cache for named routes
                VirtualPathData cachedNamed;
                if (TryGetCachedVpd(requestContext, name, area: null, values: values, result: out cachedNamed))
                {
                    return cachedNamed;
                }

                VirtualPathData named = routes.GetVirtualPath(requestContext, name, values);
                CacheVpd(requestContext, name, area: null, values: values, vpd: named);
                return named;
            }

            string targetArea = null;
            if (values != null)
            {
                object targetAreaRawValue;
                if (values.TryGetValue("area", out targetAreaRawValue))
                {
                    targetArea = targetAreaRawValue as string;
                }
                else
                {
                    // set target area to current area
                    if (requestContext != null)
                    {
                        targetArea = AreaHelpers.GetAreaName(requestContext.RouteData);
                    }
                }
            }

            // need to apply a correction to the RVD if areas are in use
            RouteValueDictionary correctedValues = values;
            RouteCollection filteredRoutes = FilterRouteCollectionByArea(routes, targetArea, out usingAreas);
            if (usingAreas)
            {
                correctedValues = new RouteValueDictionary(values);
                correctedValues.Remove("area");
            }

            // Per-request cache for unnamed routes by area + values
            VirtualPathData cached;
            if (TryGetCachedVpd(requestContext, name: null, area: targetArea, values: correctedValues, result: out cached))
            {
                return cached;
            }

            VirtualPathData vpd = filteredRoutes.GetVirtualPath(requestContext, correctedValues);
            CacheVpd(requestContext, name: null, area: targetArea, values: correctedValues, vpd: vpd);
            return vpd;
        }

        private const string VpdCacheKey = "__mvc_vpd_cache";

        private static bool TryGetCachedVpd(RequestContext requestContext, string name, string area, RouteValueDictionary values, out VirtualPathData result)
        {
            result = null;
            if (requestContext == null || requestContext.HttpContext == null)
            {
                return false;
            }

            IDictionary cache = requestContext.HttpContext.Items[VpdCacheKey] as IDictionary;
            if (cache == null)
            {
                return false;
            }

            string key = BuildVpdCacheKey(name, area, values);
            result = cache[key] as VirtualPathData;
            return result != null;
        }

        private static void CacheVpd(RequestContext requestContext, string name, string area, RouteValueDictionary values, VirtualPathData vpd)
        {
            if (vpd == null || requestContext == null || requestContext.HttpContext == null)
            {
                return;
            }

            IDictionary cache = requestContext.HttpContext.Items[VpdCacheKey] as IDictionary;
            if (cache == null)
            {
                cache = new Dictionary<string, VirtualPathData>(StringComparer.OrdinalIgnoreCase);
                requestContext.HttpContext.Items[VpdCacheKey] = cache;
            }

            string key = BuildVpdCacheKey(name, area, values);
            cache[key] = vpd;
        }

        private static string BuildVpdCacheKey(string name, string area, RouteValueDictionary values)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("name:");
            sb.Append(name ?? String.Empty);
            sb.Append("|area:");
            sb.Append(area ?? String.Empty);
            sb.Append("|values:");

            if (values != null)
            {
                // stable ordering
                foreach (var kvp in values.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                {
                    sb.Append(kvp.Key);
                    sb.Append('=');
                    sb.Append(Convert.ToString(kvp.Value, CultureInfo.InvariantCulture));
                    sb.Append(';');
                }
            }
            return sb.ToString();
        }
        
        [SuppressMessage("Microsoft.Design", "CA1054:UriParametersShouldNotBeStrings", MessageId = "1#", Justification = "This is not a regular URL as it may contain special routing characters.")]
        public static void IgnoreRoute(this RouteCollection routes, string url)
        {
            IgnoreRoute(routes, url, null /* constraints */);
        }

        [SuppressMessage("Microsoft.Design", "CA1054:UriParametersShouldNotBeStrings", MessageId = "1#", Justification = "This is not a regular URL as it may contain special routing characters.")]
        public static void IgnoreRoute(this RouteCollection routes, string url, object constraints)
        {
            if (routes == null)
            {
                throw new ArgumentNullException("routes");
            }
            if (url == null)
            {
                throw new ArgumentNullException("url");
            }

            IgnoreRouteInternal route = new IgnoreRouteInternal(url)
            {
                Constraints = CreateRouteValueDictionaryUncached(constraints)
            };

            ConstraintValidation.Validate(route);

            routes.Add(route);
        }

        [SuppressMessage("Microsoft.Design", "CA1054:UriParametersShouldNotBeStrings", MessageId = "2#", Justification = "This is not a regular URL as it may contain special routing characters.")]
        public static Route MapRoute(this RouteCollection routes, string name, string url)
        {
            return MapRoute(routes, name, url, null /* defaults */, (object)null /* constraints */);
        }

        [SuppressMessage("Microsoft.Design", "CA1054:UriParametersShouldNotBeStrings", MessageId = "2#", Justification = "This is not a regular URL as it may contain special routing characters.")]
        public static Route MapRoute(this RouteCollection routes, string name, string url, object defaults)
        {
            return MapRoute(routes, name, url, defaults, (object)null /* constraints */);
        }

        [SuppressMessage("Microsoft.Design", "CA1054:UriParametersShouldNotBeStrings", MessageId = "2#", Justification = "This is not a regular URL as it may contain special routing characters.")]
        public static Route MapRoute(this RouteCollection routes, string name, string url, object defaults, object constraints)
        {
            return MapRoute(routes, name, url, defaults, constraints, null /* namespaces */);
        }

        [SuppressMessage("Microsoft.Design", "CA1054:UriParametersShouldNotBeStrings", MessageId = "2#", Justification = "This is not a regular URL as it may contain special routing characters.")]
        public static Route MapRoute(this RouteCollection routes, string name, string url, string[] namespaces)
        {
            return MapRoute(routes, name, url, null /* defaults */, null /* constraints */, namespaces);
        }

        [SuppressMessage("Microsoft.Design", "CA1054:UriParametersShouldNotBeStrings", MessageId = "2#", Justification = "This is not a regular URL as it may contain special routing characters.")]
        public static Route MapRoute(this RouteCollection routes, string name, string url, object defaults, string[] namespaces)
        {
            return MapRoute(routes, name, url, defaults, null /* constraints */, namespaces);
        }

        [SuppressMessage("Microsoft.Design", "CA1054:UriParametersShouldNotBeStrings", MessageId = "2#", Justification = "This is not a regular URL as it may contain special routing characters.")]
        public static Route MapRoute(this RouteCollection routes, string name, string url, object defaults, object constraints, string[] namespaces)
        {
            if (routes == null)
            {
                throw new ArgumentNullException("routes");
            }
            if (url == null)
            {
                throw new ArgumentNullException("url");
            }

            Route route = new Route(url, new MvcRouteHandler())
            {
                Defaults = CreateRouteValueDictionaryUncached(defaults),
                Constraints = CreateRouteValueDictionaryUncached(constraints),
                DataTokens = new RouteValueDictionary()
            };

            ConstraintValidation.Validate(route);

            if ((namespaces != null) && (namespaces.Length > 0))
            {
                route.DataTokens[RouteDataTokenKeys.Namespaces] = namespaces;
            }

            routes.Add(name, route);

            return route;
        }

        /// <summary>
        /// The callers to this method are used at startup only, thus it's a bit better to use
        /// the uncached method because it will run faster for the first few times, and will not
        /// consume memory long term.
        /// </summary>
        private static RouteValueDictionary CreateRouteValueDictionaryUncached(object values)
        {
            var dictionary = values as IDictionary<string, object>;
            if (dictionary != null)
            {
                return new RouteValueDictionary(dictionary);
            }

            return TypeHelper.ObjectToDictionaryUncached(values);
        }

        private sealed class IgnoreRouteInternal : Route
        {
            public IgnoreRouteInternal(string url)
                : base(url, new StopRoutingHandler())
            {
            }

            public override VirtualPathData GetVirtualPath(RequestContext requestContext, RouteValueDictionary routeValues)
            {
                // Never match during route generation. This avoids the scenario where an IgnoreRoute with
                // fairly relaxed constraints ends up eagerly matching all generated URLs.
                return null;
            }
        }
    }
}
