using System.Runtime.InteropServices;

namespace UltraFastRouting.Core
{
    /// <summary>
    /// Represents a route match result with zero-allocation design.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct RouteMatch
    {
        /// <summary>
        /// Gets whether the route matched successfully.
        /// </summary>
        public readonly bool IsMatch { get; }

        /// <summary>
        /// Gets the controller name from the matched route.
        /// </summary>
        public readonly string Controller { get; }

        /// <summary>
        /// Gets the action name from the matched route.
        /// </summary>
        public readonly string Action { get; }

        /// <summary>
        /// Gets the route parameters as an array for zero-allocation access.
        /// </summary>
        public readonly KeyValuePair<string, string>[] Parameters { get; }

        /// <summary>
        /// Initializes a new instance of the RouteMatch struct.
        /// </summary>
        /// <param name="isMatch">Whether the route matched.</param>
        /// <param name="controller">The controller name.</param>
        /// <param name="action">The action name.</param>
        /// <param name="parameters">The route parameters.</param>
        public RouteMatch(bool isMatch, string controller, string action, KeyValuePair<string, string>[] parameters)
        {
            IsMatch = isMatch;
            Controller = controller;
            Action = action;
            Parameters = parameters;
        }

        /// <summary>
        /// Creates a no-match result.
        /// </summary>
        /// <returns>A RouteMatch representing no match.</returns>
        public static RouteMatch NoMatch() => new(false, "", "", Array.Empty<KeyValuePair<string, string>>());

        /// <summary>
        /// Creates a successful match result.
        /// </summary>
        /// <param name="controller">The controller name.</param>
        /// <param name="action">The action name.</param>
        /// <param name="parameters">The route parameters.</param>
        /// <returns>A RouteMatch representing a successful match.</returns>
        public static RouteMatch Success(string controller, string action, KeyValuePair<string, string>[] parameters) 
            => new(true, controller, action, parameters);
    }
}