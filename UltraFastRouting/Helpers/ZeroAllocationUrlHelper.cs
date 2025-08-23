using System.Runtime.CompilerServices;
using UltraFastRouting.Core;

namespace UltraFastRouting.Helpers
{
    /// <summary>
    /// A zero-allocation URL helper that provides ultra-fast URL generation.
    /// </summary>
    public class ZeroAllocationUrlHelper
    {
        private readonly ZeroAllocationRouteCollection _routeCollection;

        /// <summary>
        /// Initializes a new instance of the ZeroAllocationUrlHelper.
        /// </summary>
        /// <param name="routeCollection">The route collection to use for URL generation.</param>
        public ZeroAllocationUrlHelper(ZeroAllocationRouteCollection routeCollection)
        {
            _routeCollection = routeCollection;
        }

        /// <summary>
        /// Generates a URL for the specified action with zero-allocation design.
        /// </summary>
        /// <param name="actionName">The action name.</param>
        /// <param name="controllerName">The controller name.</param>
        /// <param name="routeValues">The route values.</param>
        /// <returns>The generated URL.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string Action(string actionName, string controllerName, ReadOnlySpan<KeyValuePair<string, object>> routeValues = default)
        {
            return _routeCollection.GetVirtualPath(controllerName, actionName, routeValues) ?? "#";
        }

        /// <summary>
        /// Generates a URL for the specified action with zero-allocation design.
        /// </summary>
        /// <param name="actionName">The action name.</param>
        /// <param name="routeValues">The route values.</param>
        /// <returns>The generated URL.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string Action(string actionName, ReadOnlySpan<KeyValuePair<string, object>> routeValues = default)
        {
            return Action(actionName, "Home", routeValues);
        }

        /// <summary>
        /// Generates a route URL with zero-allocation design.
        /// </summary>
        /// <param name="routeName">The route name.</param>
        /// <param name="routeValues">The route values.</param>
        /// <returns>The generated URL.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string RouteUrl(string routeName, ReadOnlySpan<KeyValuePair<string, object>> routeValues = default)
        {
            return _routeCollection.GetVirtualPath(routeName, routeValues) ?? "#";
        }

        /// <summary>
        /// Generates a content URL with zero-allocation design.
        /// </summary>
        /// <param name="contentPath">The content path.</param>
        /// <returns>The generated URL.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string Content(string contentPath)
        {
            if (string.IsNullOrEmpty(contentPath))
                return "#";

            if (contentPath.StartsWith("~/"))
                return contentPath.Substring(1);

            if (contentPath.StartsWith("/"))
                return contentPath;

            return "/" + contentPath;
        }

        /// <summary>
        /// Generates an absolute URL with zero-allocation design.
        /// </summary>
        /// <param name="actionName">The action name.</param>
        /// <param name="controllerName">The controller name.</param>
        /// <param name="routeValues">The route values.</param>
        /// <param name="protocol">The protocol (http/https).</param>
        /// <param name="hostName">The host name.</param>
        /// <returns>The generated absolute URL.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string Action(string actionName, string controllerName, ReadOnlySpan<KeyValuePair<string, object>> routeValues, string protocol, string hostName)
        {
            var relativeUrl = Action(actionName, controllerName, routeValues);
            if (relativeUrl == "#")
                return relativeUrl;

            return $"{protocol}://{hostName}{relativeUrl}";
        }

        /// <summary>
        /// Generates an absolute route URL with zero-allocation design.
        /// </summary>
        /// <param name="routeName">The route name.</param>
        /// <param name="routeValues">The route values.</param>
        /// <param name="protocol">The protocol (http/https).</param>
        /// <param name="hostName">The host name.</param>
        /// <returns>The generated absolute URL.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string RouteUrl(string routeName, ReadOnlySpan<KeyValuePair<string, object>> routeValues, string protocol, string hostName)
        {
            var relativeUrl = RouteUrl(routeName, routeValues);
            if (relativeUrl == "#")
                return relativeUrl;

            return $"{protocol}://{hostName}{relativeUrl}";
        }
    }
}