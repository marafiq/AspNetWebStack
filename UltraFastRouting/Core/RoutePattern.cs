using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace UltraFastRouting.Core
{
    /// <summary>
    /// Represents a route pattern with zero-allocation design.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct RoutePattern
    {
        /// <summary>
        /// Gets the route pattern string.
        /// </summary>
        public readonly string Pattern { get; }

        /// <summary>
        /// Gets the compiled regex for pattern matching.
        /// </summary>
        public readonly Regex CompiledRegex { get; }

        /// <summary>
        /// Gets the controller name.
        /// </summary>
        public readonly string Controller { get; }

        /// <summary>
        /// Gets the action name.
        /// </summary>
        public readonly string Action { get; }

        /// <summary>
        /// Gets the route name.
        /// </summary>
        public readonly string Name { get; }

        /// <summary>
        /// Gets the route priority (higher values have higher priority).
        /// </summary>
        public readonly int Priority { get; }

        /// <summary>
        /// Gets the default values as an array.
        /// </summary>
        public readonly KeyValuePair<string, string>[] Defaults { get; }

        /// <summary>
        /// Gets the constraints as an array.
        /// </summary>
        public readonly KeyValuePair<string, string>[] Constraints { get; }

        /// <summary>
        /// Initializes a new instance of the RoutePattern struct.
        /// </summary>
        /// <param name="pattern">The route pattern string.</param>
        /// <param name="regex">The compiled regex for matching.</param>
        /// <param name="controller">The controller name.</param>
        /// <param name="action">The action name.</param>
        /// <param name="name">The route name.</param>
        /// <param name="priority">The route priority.</param>
        /// <param name="defaults">The default values.</param>
        /// <param name="constraints">The constraints.</param>
        public RoutePattern(string pattern, Regex regex, string controller, string action, string name, int priority, 
            KeyValuePair<string, string>[] defaults, KeyValuePair<string, string>[] constraints)
        {
            Pattern = pattern;
            CompiledRegex = regex;
            Controller = controller;
            Action = action;
            Name = name;
            Priority = priority;
            Defaults = defaults;
            Constraints = constraints;
        }

        /// <summary>
        /// Creates a simple route pattern.
        /// </summary>
        /// <param name="pattern">The route pattern.</param>
        /// <param name="controller">The controller name.</param>
        /// <param name="action">The action name.</param>
        /// <param name="name">The route name.</param>
        /// <returns>A new RoutePattern instance.</returns>
        public static RoutePattern Create(string pattern, string controller, string action, string name = "")
        {
            var regex = CreateRegexFromPattern(pattern);
            return new RoutePattern(pattern, regex, controller, action, name, 1, 
                Array.Empty<KeyValuePair<string, string>>(), 
                Array.Empty<KeyValuePair<string, string>>());
        }

        /// <summary>
        /// Creates a regex pattern from a route pattern string.
        /// </summary>
        /// <param name="pattern">The route pattern.</param>
        /// <returns>A compiled regex for the pattern.</returns>
        public static Regex CreateRegexFromPattern(string pattern)
        {
            var regexPattern = "^" + pattern
                .Replace("{id}", "(?<id>\\d+)")
                .Replace("{slug}", "(?<slug>[a-zA-Z0-9-]+)")
                .Replace("{category}", "(?<category>[a-zA-Z]+)")
                .Replace("{param1}", "(?<param1>[a-zA-Z0-9]+)")
                .Replace("{param2}", "(?<param2>[a-zA-Z0-9]+)")
                .Replace("{param3}", "(?<param3>[a-zA-Z0-9]+)")
                .Replace("{version}", "(?<version>\\d+)")
                .Replace("{format}", "(?<format>json|xml|html)")
                .Replace("{subaction}", "(?<subaction>[a-zA-Z]+)")
                .Replace("{subcontroller}", "(?<subcontroller>[a-zA-Z]+)")
                .Replace("{query}", "(?<query>[a-zA-Z0-9%]+)")
                .Replace("{page}", "(?<page>\\d+)")
                .Replace("{path}", "(?<path>[a-zA-Z0-9/]+)")
                .Replace("{filename}", "(?<filename>[a-zA-Z0-9.-]+)")
                .Replace("{type}", "(?<type>[a-zA-Z]+)")
                .Replace("{date}", "(?<date>\\d{4}-\\d{2}-\\d{2})")
                .Replace("{metric}", "(?<metric>[a-zA-Z]+)")
                .Replace("{period}", "(?<period>daily|weekly|monthly|yearly)")
                .Replace("{threadId}", "(?<threadId>\\d+)")
                .Replace("{messageId}", "(?<messageId>\\d+)")
                .Replace("{eventId}", "(?<eventId>\\d+)")
                .Replace("{attendeeId}", "(?<attendeeId>\\d+)")
                .Replace("{projectId}", "(?<projectId>\\d+)")
                .Replace("{taskId}", "(?<taskId>\\d+)")
                .Replace("{teamId}", "(?<teamId>\\d+)")
                .Replace("{memberId}", "(?<memberId>\\d+)")
                .Replace("{companyId}", "(?<companyId>\\d+)")
                .Replace("{deptId}", "(?<deptId>\\d+)")
                .Replace("{customerId}", "(?<customerId>\\d+)")
                .Replace("{orderId}", "(?<orderId>\\d+)")
                .Replace("{warehouseId}", "(?<warehouseId>\\d+)")
                .Replace("{itemId}", "(?<itemId>\\d+)")
                .Replace("{accountId}", "(?<accountId>\\d+)")
                .Replace("{txnId}", "(?<txnId>\\d+)")
                .Replace("{employeeId}", "(?<employeeId>\\d+)")
                .Replace("{docId}", "(?<docId>\\d+)")
                .Replace("{username}", "(?<username>[a-zA-Z0-9_]+)")
                .Replace("{year}", "(?<year>\\d{4})")
                .Replace("{month}", "(?<month>\\d{2})")
                + "$";

            return new Regex(regexPattern, RegexOptions.Compiled);
        }
    }
}