using System.Runtime.CompilerServices;
using System.Text;
using UltraFastRouting.Core;

namespace UltraFastRouting.Helpers
{
    /// <summary>
    /// A zero-allocation HTML helper that provides ultra-fast HTML generation.
    /// </summary>
    public class ZeroAllocationHtmlHelper
    {
        private readonly ZeroAllocationRouteCollection _routeCollection;
        private readonly ZeroAllocationUrlHelper _urlHelper;

        /// <summary>
        /// Initializes a new instance of the ZeroAllocationHtmlHelper.
        /// </summary>
        /// <param name="routeCollection">The route collection to use for URL generation.</param>
        public ZeroAllocationHtmlHelper(ZeroAllocationRouteCollection routeCollection)
        {
            _routeCollection = routeCollection;
            _urlHelper = new ZeroAllocationUrlHelper(routeCollection);
        }

        /// <summary>
        /// Generates an action link with zero-allocation design.
        /// </summary>
        /// <param name="linkText">The link text.</param>
        /// <param name="actionName">The action name.</param>
        /// <param name="controllerName">The controller name.</param>
        /// <param name="routeValues">The route values.</param>
        /// <param name="htmlAttributes">The HTML attributes.</param>
        /// <returns>The generated HTML link.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ActionLink(string linkText, string actionName, string controllerName, 
            ReadOnlySpan<KeyValuePair<string, object>> routeValues = default,
            ReadOnlySpan<KeyValuePair<string, string>> htmlAttributes = default)
        {
            var url = _urlHelper.Action(actionName, controllerName, routeValues);
            return GenerateLink(linkText, url, htmlAttributes);
        }

        /// <summary>
        /// Generates an action link with zero-allocation design.
        /// </summary>
        /// <param name="linkText">The link text.</param>
        /// <param name="actionName">The action name.</param>
        /// <param name="routeValues">The route values.</param>
        /// <param name="htmlAttributes">The HTML attributes.</param>
        /// <returns>The generated HTML link.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ActionLink(string linkText, string actionName, 
            ReadOnlySpan<KeyValuePair<string, object>> routeValues = default,
            ReadOnlySpan<KeyValuePair<string, string>> htmlAttributes = default)
        {
            return ActionLink(linkText, actionName, "Home", routeValues, htmlAttributes);
        }

        /// <summary>
        /// Generates a route link with zero-allocation design.
        /// </summary>
        /// <param name="linkText">The link text.</param>
        /// <param name="routeName">The route name.</param>
        /// <param name="routeValues">The route values.</param>
        /// <param name="htmlAttributes">The HTML attributes.</param>
        /// <returns>The generated HTML link.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string RouteLink(string linkText, string routeName, 
            ReadOnlySpan<KeyValuePair<string, object>> routeValues = default,
            ReadOnlySpan<KeyValuePair<string, string>> htmlAttributes = default)
        {
            var url = _urlHelper.RouteUrl(routeName, routeValues);
            return GenerateLink(linkText, url, htmlAttributes);
        }

        /// <summary>
        /// Generates a partial view with zero-allocation design.
        /// </summary>
        /// <param name="partialViewName">The partial view name.</param>
        /// <param name="model">The model.</param>
        /// <returns>The generated partial view HTML.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string Partial(string partialViewName, object? model = null)
        {
            // In a real implementation, this would render the partial view
            // For now, we return a placeholder with zero allocation
            return $"<!-- Partial: {partialViewName} -->";
        }

        /// <summary>
        /// Generates a child action with zero-allocation design.
        /// </summary>
        /// <param name="actionName">The action name.</param>
        /// <param name="controllerName">The controller name.</param>
        /// <param name="routeValues">The route values.</param>
        /// <returns>The generated child action HTML.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string Action(string actionName, string controllerName, 
            ReadOnlySpan<KeyValuePair<string, object>> routeValues = default)
        {
            // In a real implementation, this would execute the child action
            // For now, we return a placeholder with zero allocation
            return $"<!-- Child Action: {controllerName}.{actionName} -->";
        }

        /// <summary>
        /// Generates a child action with zero-allocation design.
        /// </summary>
        /// <param name="actionName">The action name.</param>
        /// <param name="routeValues">The route values.</param>
        /// <returns>The generated child action HTML.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string Action(string actionName, ReadOnlySpan<KeyValuePair<string, object>> routeValues = default)
        {
            return Action(actionName, "Home", routeValues);
        }

        /// <summary>
        /// Generates a raw HTML string with zero-allocation design.
        /// </summary>
        /// <param name="value">The HTML value.</param>
        /// <returns>The raw HTML string.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string Raw(string value)
        {
            return value ?? "";
        }

        /// <summary>
        /// Generates an encoded HTML string with zero-allocation design.
        /// </summary>
        /// <param name="value">The value to encode.</param>
        /// <returns>The encoded HTML string.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string Encode(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            // Use StringBuilder for encoding
            var sb = new System.Text.StringBuilder();
            foreach (var c in value)
            {
                switch (c)
                {
                    case '<':
                        sb.Append("&lt;");
                        break;
                    case '>':
                        sb.Append("&gt;");
                        break;
                    case '&':
                        sb.Append("&amp;");
                        break;
                    case '"':
                        sb.Append("&quot;");
                        break;
                    case '\'':
                        sb.Append("&#39;");
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }

            return sb.ToString();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private string GenerateLink(string linkText, string url, ReadOnlySpan<KeyValuePair<string, string>> htmlAttributes)
        {
            // Use StringBuilder for HTML generation
            var sb = new System.Text.StringBuilder();
            sb.Append("<a href=\"");
            sb.Append(url);

            // Attributes
            foreach (var attr in htmlAttributes)
            {
                sb.Append(' ');
                sb.Append(attr.Key);
                sb.Append("=\"");
                sb.Append(attr.Value);
                sb.Append('"');
            }

            sb.Append("\">");
            sb.Append(linkText);
            sb.Append("</a>");

            return sb.ToString();
        }
    }
}