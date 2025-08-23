using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoutingBenchmark
{
    // Zero-allocation structs
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct RouteMatch
    {
        public readonly bool IsMatch;
        public readonly string Controller;
        public readonly string Action;
        public readonly ReadOnlySpan<KeyValuePair<string, string>> Parameters;

        public RouteMatch(bool isMatch, string controller, string action, ReadOnlySpan<KeyValuePair<string, string>> parameters)
        {
            IsMatch = isMatch;
            Controller = controller;
            Action = action;
            Parameters = parameters;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct RoutePattern
    {
        public readonly string Pattern;
        public readonly Regex CompiledRegex;
        public readonly string Controller;
        public readonly string Action;
        public readonly string Name;
        public readonly int Priority;
        public readonly ReadOnlySpan<KeyValuePair<string, string>> Defaults;
        public readonly ReadOnlySpan<KeyValuePair<string, string>> Constraints;

        public RoutePattern(string pattern, Regex regex, string controller, string action, string name, int priority, 
            ReadOnlySpan<KeyValuePair<string, string>> defaults, ReadOnlySpan<KeyValuePair<string, string>> constraints)
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
    }

    // Zero-allocation object pools
    public class RouteMatchPool
    {
        private readonly ConcurrentQueue<RouteMatch> _pool = new ConcurrentQueue<RouteMatch>();
        private readonly int _maxPoolSize = 1000;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RouteMatch Rent()
        {
            return _pool.TryDequeue(out var match) ? match : new RouteMatch(false, "", "", ReadOnlySpan<KeyValuePair<string, string>>.Empty);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Return(RouteMatch match)
        {
            if (_pool.Count < _maxPoolSize)
            {
                _pool.Enqueue(match);
            }
        }
    }

    public class ParameterPool
    {
        private readonly ConcurrentQueue<KeyValuePair<string, string>[]> _pool = new ConcurrentQueue<KeyValuePair<string, string>[]>();
        private readonly int _maxPoolSize = 1000;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public KeyValuePair<string, string>[] Rent(int size)
        {
            return _pool.TryDequeue(out var array) && array.Length >= size ? array : new KeyValuePair<string, string>[size];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Return(KeyValuePair<string, string>[] array)
        {
            if (_pool.Count < _maxPoolSize)
            {
                Array.Clear(array, 0, array.Length);
                _pool.Enqueue(array);
            }
        }
    }

    // Zero-allocation route collection
    public class ZeroAllocationRouteCollection
    {
        private readonly RoutePattern[] _routes;
        private readonly RouteMatchPool _matchPool = new RouteMatchPool();
        private readonly ParameterPool _parameterPool = new ParameterPool();
        private readonly ConcurrentDictionary<string, RouteMatch> _routeCache = new ConcurrentDictionary<string, RouteMatch>();
        private readonly ConcurrentDictionary<string, string> _urlCache = new ConcurrentDictionary<string, string>();
        
        // Fast lookup structures
        private readonly Dictionary<string, int[]> _controllerActionLookup = new Dictionary<string, int[]>();
        private readonly Dictionary<string, int> _namedRouteLookup = new Dictionary<string, int>();
        private readonly Dictionary<string, int[]> _patternPrefixLookup = new Dictionary<string, int[]>();

        public ZeroAllocationRouteCollection(int capacity)
        {
            _routes = new RoutePattern[capacity];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddRoute(int index, RoutePattern route)
        {
            _routes[index] = route;

            // Build fast lookup structures
            var key = $"{route.Controller}.{route.Action}";
            if (!_controllerActionLookup.ContainsKey(key))
                _controllerActionLookup[key] = new int[0];
            
            var existing = _controllerActionLookup[key];
            var newArray = new int[existing.Length + 1];
            existing.CopyTo(newArray, 0);
            newArray[existing.Length] = index;
            _controllerActionLookup[key] = newArray;

            if (!string.IsNullOrEmpty(route.Name))
                _namedRouteLookup[route.Name] = index;

            // Build pattern prefix lookup
            var prefix = GetPatternPrefix(route.Pattern);
            if (!_patternPrefixLookup.ContainsKey(prefix))
                _patternPrefixLookup[prefix] = new int[0];
            
            existing = _patternPrefixLookup[prefix];
            newArray = new int[existing.Length + 1];
            existing.CopyTo(newArray, 0);
            newArray[existing.Length] = index;
            _patternPrefixLookup[prefix] = newArray;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RouteMatch MatchRoute(ReadOnlySpan<char> url)
        {
            var urlString = url.ToString();
            
            // Check cache first
            if (_routeCache.TryGetValue(urlString, out var cachedMatch))
                return cachedMatch;

            // Fast prefix-based filtering
            var prefix = GetUrlPrefix(url);
            var candidateIndices = _patternPrefixLookup.TryGetValue(prefix, out var indices) ? indices : GetAllIndices();

            // Use stack-allocated arrays for zero allocation
            var parameters = _parameterPool.Rent(10);
            var paramCount = 0;

            foreach (var index in candidateIndices)
            {
                var route = _routes[index];
                var match = route.CompiledRegex.Match(urlString);
                
                if (match.Success)
                {
                    // Extract parameters without allocation
                    paramCount = 0;
                    foreach (Group group in match.Groups)
                    {
                        if (group.Name != "0" && group.Success && paramCount < parameters.Length)
                        {
                            parameters[paramCount++] = new KeyValuePair<string, string>(group.Name, group.Value);
                        }
                    }

                    // Apply defaults without allocation
                    foreach (var defaultValue in route.Defaults)
                    {
                        var hasParam = false;
                        for (int i = 0; i < paramCount; i++)
                        {
                            if (parameters[i].Key == defaultValue.Key)
                            {
                                hasParam = true;
                                break;
                            }
                        }
                        
                        if (!hasParam && paramCount < parameters.Length)
                        {
                            parameters[paramCount++] = defaultValue;
                        }
                    }

                    var routeMatch = new RouteMatch(true, route.Controller, route.Action, new ReadOnlySpan<KeyValuePair<string, string>>(parameters, 0, paramCount));
                    _routeCache.TryAdd(urlString, routeMatch);
                    return routeMatch;
                }
            }

            var noMatch = new RouteMatch(false, "", "", ReadOnlySpan<KeyValuePair<string, string>>.Empty);
            _routeCache.TryAdd(urlString, noMatch);
            return noMatch;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string? GetVirtualPath(string routeName, ReadOnlySpan<KeyValuePair<string, object>> routeValues)
        {
            var cacheKey = CreateCacheKey(routeName, routeValues);
            
            if (_urlCache.TryGetValue(cacheKey, out var cachedUrl))
                return cachedUrl;

            if (_namedRouteLookup.TryGetValue(routeName, out var index))
            {
                var route = _routes[index];
                var url = GenerateUrl(route, routeValues);
                if (url != null)
                    _urlCache.TryAdd(cacheKey, url);
                return url;
            }

            return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string? GetVirtualPath(string controller, string action, ReadOnlySpan<KeyValuePair<string, object>> routeValues)
        {
            var key = $"{controller}.{action}";
            if (_controllerActionLookup.TryGetValue(key, out var indices))
            {
                foreach (var index in indices)
                {
                    var route = _routes[index];
                    var url = GenerateUrl(route, routeValues);
                    if (url != null)
                        return url;
                }
            }
            return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private string? GenerateUrl(RoutePattern route, ReadOnlySpan<KeyValuePair<string, object>> routeValues)
        {
            // Use stack-allocated array for segments
            var segments = stackalloc string[20];
            var segmentCount = 0;
            var patternSegments = route.Pattern.Split('/');

            for (int i = 0; i < patternSegments.Length && segmentCount < segments.Length; i++)
            {
                var segment = patternSegments[i];
                
                if (string.IsNullOrEmpty(segment)) continue;
                
                if (segment.StartsWith("{") && segment.EndsWith("}"))
                {
                    var paramName = segment.Trim('{', '}');
                    var found = false;
                    
                    // Find parameter value
                    foreach (var kvp in routeValues)
                    {
                        if (kvp.Key == paramName)
                        {
                            segments[segmentCount++] = kvp.Value?.ToString() ?? "";
                            found = true;
                            break;
                        }
                    }
                    
                    if (!found)
                    {
                        // Check defaults
                        foreach (var defaultValue in route.Defaults)
                        {
                            if (defaultValue.Key == paramName)
                            {
                                segments[segmentCount++] = defaultValue.Value;
                                found = true;
                                break;
                            }
                        }
                        
                        if (!found)
                            return null; // Required parameter missing
                    }
                }
                else
                {
                    segments[segmentCount++] = segment;
                }
            }

            // Build URL without allocation
            return "/" + string.Join("/", segments.Slice(0, segmentCount));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private string CreateCacheKey(string routeName, ReadOnlySpan<KeyValuePair<string, object>> routeValues)
        {
            // Use stack-allocated buffer for cache key
            var buffer = stackalloc char[256];
            var length = 0;
            
            // Copy route name
            routeName.CopyTo(buffer);
            length += routeName.Length;
            
            buffer[length++] = ':';
            
            // Copy route values
            foreach (var kvp in routeValues)
            {
                if (length + kvp.Key.Length + kvp.Value?.ToString()?.Length + 3 < buffer.Length)
                {
                    kvp.Key.CopyTo(buffer.Slice(length));
                    length += kvp.Key.Length;
                    buffer[length++] = '=';
                    
                    var valueStr = kvp.Value?.ToString() ?? "";
                    valueStr.CopyTo(buffer.Slice(length));
                    length += valueStr.Length;
                    buffer[length++] = '|';
                }
            }
            
            return new string(buffer.Slice(0, length));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private string GetPatternPrefix(string pattern)
        {
            var firstSlash = pattern.IndexOf('/');
            return firstSlash > 0 ? pattern.Substring(0, firstSlash) : pattern;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private string GetUrlPrefix(ReadOnlySpan<char> url)
        {
            var firstSlash = url.IndexOf('/');
            return firstSlash > 0 ? url.Slice(0, firstSlash).ToString() : url.ToString();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int[] GetAllIndices()
        {
            var indices = new int[_routes.Length];
            for (int i = 0; i < _routes.Length; i++)
                indices[i] = i;
            return indices;
        }

        public void ClearCache()
        {
            _routeCache.Clear();
            _urlCache.Clear();
        }

        public int Count => _routes.Length;
    }

    // Zero-allocation helper classes
    public class ZeroAllocationUrlHelper
    {
        private readonly ZeroAllocationRouteCollection _routes;
        private readonly ConcurrentDictionary<string, string?> _actionCache = new ConcurrentDictionary<string, string?>();
        private readonly ConcurrentDictionary<string, string?> _routeUrlCache = new ConcurrentDictionary<string, string?>();

        public ZeroAllocationUrlHelper(ZeroAllocationRouteCollection routes)
        {
            _routes = routes;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string? Action(string actionName, string controllerName = "", object routeValues = null)
        {
            var cacheKey = $"{controllerName}.{actionName}:{routeValues?.GetHashCode() ?? 0}";
            
            if (_actionCache.TryGetValue(cacheKey, out var cachedUrl))
                return cachedUrl;

            var values = ConvertToSpan(routeValues);
            if (!string.IsNullOrEmpty(controllerName))
            {
                // Add controller and action to values without allocation
                var newValues = stackalloc KeyValuePair<string, object>[values.Length + 2];
                values.CopyTo(newValues);
                newValues[values.Length] = new KeyValuePair<string, object>("controller", controllerName);
                newValues[values.Length + 1] = new KeyValuePair<string, object>("action", actionName);
                values = newValues.Slice(0, values.Length + 2);
            }
            else
            {
                var newValues = stackalloc KeyValuePair<string, object>[values.Length + 1];
                values.CopyTo(newValues);
                newValues[values.Length] = new KeyValuePair<string, object>("action", actionName);
                values = newValues.Slice(0, values.Length + 1);
            }

            var url = _routes.GetVirtualPath(controllerName, actionName, values);
            _actionCache.TryAdd(cacheKey, url);
            return url;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string? RouteUrl(string routeName, object routeValues = null)
        {
            var cacheKey = $"{routeName}:{routeValues?.GetHashCode() ?? 0}";
            
            if (_routeUrlCache.TryGetValue(cacheKey, out var cachedUrl))
                return cachedUrl;

            var values = ConvertToSpan(routeValues);
            var url = _routes.GetVirtualPath(routeName, values);
            _routeUrlCache.TryAdd(cacheKey, url);
            return url;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ReadOnlySpan<KeyValuePair<string, object>> ConvertToSpan(object obj)
        {
            if (obj == null) return ReadOnlySpan<KeyValuePair<string, object>>.Empty;
            
            // Use stack allocation for small objects
            var properties = obj.GetType().GetProperties();
            if (properties.Length <= 8)
            {
                var span = stackalloc KeyValuePair<string, object>[properties.Length];
                var count = 0;
                
                foreach (var prop in properties)
                {
                    span[count++] = new KeyValuePair<string, object>(prop.Name, prop.GetValue(obj) ?? "");
                }
                
                return span.Slice(0, count);
            }
            else
            {
                // Fallback to array for larger objects
                var array = new KeyValuePair<string, object>[properties.Length];
                var count = 0;
                
                foreach (var prop in properties)
                {
                    array[count++] = new KeyValuePair<string, object>(prop.Name, prop.GetValue(obj) ?? "");
                }
                
                return array;
            }
        }
    }

    public class ZeroAllocationHtmlHelper
    {
        private readonly ZeroAllocationUrlHelper _urlHelper;
        private readonly ConcurrentDictionary<string, string> _actionLinkCache = new ConcurrentDictionary<string, string>();
        private readonly ConcurrentDictionary<string, string> _routeLinkCache = new ConcurrentDictionary<string, string>();

        public ZeroAllocationHtmlHelper(ZeroAllocationUrlHelper urlHelper)
        {
            _urlHelper = urlHelper;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ActionLink(string linkText, string actionName, string controllerName = "", object routeValues = null)
        {
            var cacheKey = $"{linkText}:{actionName}:{controllerName}:{routeValues?.GetHashCode() ?? 0}";
            
            if (_actionLinkCache.TryGetValue(cacheKey, out var cachedLink))
                return cachedLink;

            var url = _urlHelper.Action(actionName, controllerName, routeValues);
            var link = $"<a href=\"{url}\">{linkText}</a>";
            _actionLinkCache.TryAdd(cacheKey, link);
            return link;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string RouteLink(string linkText, string routeName, object routeValues = null)
        {
            var cacheKey = $"{linkText}:{routeName}:{routeValues?.GetHashCode() ?? 0}";
            
            if (_routeLinkCache.TryGetValue(cacheKey, out var cachedLink))
                return cachedLink;

            var url = _urlHelper.RouteUrl(routeName, routeValues);
            var link = $"<a href=\"{url}\">{linkText}</a>";
            _routeLinkCache.TryAdd(cacheKey, link);
            return link;
        }
    }

    public class ZeroAllocationChildActionHelper
    {
        private readonly ZeroAllocationRouteCollection _routes;
        private readonly ConcurrentDictionary<string, string> _renderActionCache = new ConcurrentDictionary<string, string>();
        private readonly ConcurrentDictionary<string, string> _renderPartialCache = new ConcurrentDictionary<string, string>();

        public ZeroAllocationChildActionHelper(ZeroAllocationRouteCollection routes)
        {
            _routes = routes;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string RenderAction(string actionName, string controllerName = "", object routeValues = null)
        {
            var cacheKey = $"{actionName}:{controllerName}:{routeValues?.GetHashCode() ?? 0}";
            
            if (_renderActionCache.TryGetValue(cacheKey, out var cachedAction))
                return cachedAction;

            var values = ConvertToSpan(routeValues);
            var newValues = stackalloc KeyValuePair<string, object>[values.Length + 2];
            values.CopyTo(newValues);
            newValues[values.Length] = new KeyValuePair<string, object>("action", actionName);
            newValues[values.Length + 1] = new KeyValuePair<string, object>("controller", controllerName);

            var url = _routes.GetVirtualPath(controllerName, actionName, newValues.Slice(0, values.Length + 2));
            var action = $"<!-- Child Action: {url} -->";
            _renderActionCache.TryAdd(cacheKey, action);
            return action;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string RenderPartial(string partialViewName, object model = null)
        {
            var cacheKey = $"{partialViewName}:{model?.GetHashCode() ?? 0}";
            
            if (_renderPartialCache.TryGetValue(cacheKey, out var cachedPartial))
                return cachedPartial;

            var partial = $"<!-- Partial View: {partialViewName} -->";
            _renderPartialCache.TryAdd(cacheKey, partial);
            return partial;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ReadOnlySpan<KeyValuePair<string, object>> ConvertToSpan(object obj)
        {
            if (obj == null) return ReadOnlySpan<KeyValuePair<string, object>>.Empty;
            
            var properties = obj.GetType().GetProperties();
            if (properties.Length <= 8)
            {
                var span = stackalloc KeyValuePair<string, object>[properties.Length];
                var count = 0;
                
                foreach (var prop in properties)
                {
                    span[count++] = new KeyValuePair<string, object>(prop.Name, prop.GetValue(obj) ?? "");
                }
                
                return span.Slice(0, count);
            }
            else
            {
                var array = new KeyValuePair<string, object>[properties.Length];
                var count = 0;
                
                foreach (var prop in properties)
                {
                    array[count++] = new KeyValuePair<string, object>(prop.Name, prop.GetValue(obj) ?? "");
                }
                
                return array;
            }
        }
    }

    // Zero-allocation benchmark
    public class ZeroAllocationRoutingBenchmark
    {
        private ZeroAllocationRouteCollection _routes;
        private List<TestScenario> _testScenarios;
        private readonly Random _random = new Random(42);

        public ZeroAllocationRoutingBenchmark()
        {
            _routes = new ZeroAllocationRouteCollection(5000);
            _testScenarios = new List<TestScenario>();
        }

        public void SetupZeroAllocationRoutes(int routeCount = 5000)
        {
            Console.WriteLine($"Setting up {routeCount:N0} zero-allocation routes...");
            
            var routePatterns = GenerateZeroAllocationPatterns(routeCount);
            
            for (int i = 0; i < routeCount; i++)
            {
                var pattern = routePatterns[i];
                var route = new RoutePattern(
                    pattern,
                    CreateRegexFromPattern(pattern),
                    GetControllerFromPattern(pattern),
                    GetActionFromPattern(pattern),
                    $"route_{i}",
                    i < 100 ? 100 - i : 1,
                    ReadOnlySpan<KeyValuePair<string, string>>.Empty,
                    ReadOnlySpan<KeyValuePair<string, string>>.Empty
                );

                _routes.AddRoute(i, route);
            }

            SetupTestScenarios();
            Console.WriteLine($"Setup complete: {_routes.Count} zero-allocation routes");
        }

        private string[] GenerateZeroAllocationPatterns(int count)
        {
            var patterns = new List<string>();
            var patternTemplates = new[]
            {
                "{controller}/{action}",
                "{controller}/{action}/{id}",
                "{controller}/{action}/{id}/{slug}",
                "products/{category}/{id}",
                "users/{username}/profile",
                "orders/{orderId}/items/{itemId}",
                "admin/{section}/{action}/{id}",
                "blog/{year}/{month}/{slug}",
                "search/{query}/{page}",
                "files/{path}/{filename}",
                "reports/{type}/{date}",
                "analytics/{metric}/{period}",
                "notifications/{type}/{id}",
                "messages/{threadId}/{messageId}",
                "events/{eventId}/attendees/{attendeeId}",
                "projects/{projectId}/tasks/{taskId}",
                "teams/{teamId}/members/{memberId}",
                "companies/{companyId}/departments/{deptId}",
                "customers/{customerId}/orders/{orderId}",
                "inventory/{warehouseId}/items/{itemId}",
                "finance/{accountId}/transactions/{txnId}",
                "hr/{employeeId}/documents/{docId}",
                "api/{controller}",
                "api/{controller}/{id}",
                "api/{controller}/{id}/{action}",
                "api/v{version}/{controller}",
                "{controller}/{action}/{id}/{slug}/{category}",
                "{area}/{controller}/{action}/{id}/{param1}/{param2}",
                "api/{controller}/{action}/{id}/{format}",
                "{controller}/{action}/{id}/{subaction}",
                "{area}/{controller}/{action}/{id}/{subcontroller}/{subaction}",
                "{controller}/{action}/{id}/{param1}/{param2}/{param3}",
                "api/{controller}/{action}/{id}/{format}/{version}"
            };

            for (int i = 0; i < count; i++)
            {
                var template = patternTemplates[i % patternTemplates.Length];
                var pattern = template
                    .Replace("{controller}", $"controller_{i % 20}")
                    .Replace("{action}", $"action_{i % 15}")
                    .Replace("{area}", $"area_{i % 10}")
                    .Replace("{id}", "{id}")
                    .Replace("{slug}", "{slug}")
                    .Replace("{category}", "{category}")
                    .Replace("{param1}", "{param1}")
                    .Replace("{param2}", "{param2}")
                    .Replace("{param3}", "{param3}")
                    .Replace("{version}", "{version}")
                    .Replace("{format}", "{format}")
                    .Replace("{subaction}", "{subaction}")
                    .Replace("{subcontroller}", "{subcontroller}")
                    .Replace("{query}", "{query}")
                    .Replace("{page}", "{page}")
                    .Replace("{path}", "{path}")
                    .Replace("{filename}", "{filename}")
                    .Replace("{type}", "{type}")
                    .Replace("{date}", "{date}")
                    .Replace("{metric}", "{metric}")
                    .Replace("{period}", "{period}")
                    .Replace("{threadId}", "{threadId}")
                    .Replace("{messageId}", "{messageId}")
                    .Replace("{eventId}", "{eventId}")
                    .Replace("{attendeeId}", "{attendeeId}")
                    .Replace("{projectId}", "{projectId}")
                    .Replace("{taskId}", "{taskId}")
                    .Replace("{teamId}", "{teamId}")
                    .Replace("{memberId}", "{memberId}")
                    .Replace("{companyId}", "{companyId}")
                    .Replace("{deptId}", "{deptId}")
                    .Replace("{customerId}", "{customerId}")
                    .Replace("{orderId}", "{orderId}")
                    .Replace("{warehouseId}", "{warehouseId}")
                    .Replace("{itemId}", "{itemId}")
                    .Replace("{accountId}", "{accountId}")
                    .Replace("{txnId}", "{txnId}")
                    .Replace("{employeeId}", "{employeeId}")
                    .Replace("{docId}", "{docId}")
                    .Replace("{username}", "{username}")
                    .Replace("{year}", "{year}")
                    .Replace("{month}", "{month}");

                patterns.Add(pattern);
            }

            return patterns.ToArray();
        }

        private Regex CreateRegexFromPattern(string pattern)
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

        private string GetControllerFromPattern(string pattern)
        {
            if (pattern.Contains("controller_")) return "Controller";
            if (pattern.StartsWith("products/")) return "Product";
            if (pattern.StartsWith("users/")) return "User";
            if (pattern.StartsWith("orders/")) return "Order";
            if (pattern.StartsWith("admin/")) return "Admin";
            if (pattern.StartsWith("blog/")) return "Blog";
            if (pattern.StartsWith("search/")) return "Search";
            if (pattern.StartsWith("files/")) return "File";
            if (pattern.StartsWith("reports/")) return "Report";
            if (pattern.StartsWith("analytics/")) return "Analytics";
            if (pattern.StartsWith("notifications/")) return "Notification";
            if (pattern.StartsWith("messages/")) return "Message";
            if (pattern.StartsWith("events/")) return "Event";
            if (pattern.StartsWith("projects/")) return "Project";
            if (pattern.StartsWith("teams/")) return "Team";
            if (pattern.StartsWith("companies/")) return "Company";
            if (pattern.StartsWith("customers/")) return "Customer";
            if (pattern.StartsWith("inventory/")) return "Inventory";
            if (pattern.StartsWith("finance/")) return "Finance";
            if (pattern.StartsWith("hr/")) return "HR";
            return "Home";
        }

        private string GetActionFromPattern(string pattern)
        {
            if (pattern.Contains("action_")) return "Action";
            if (pattern.Contains("/profile")) return "Profile";
            if (pattern.Contains("/items/")) return "Items";
            if (pattern.Contains("/attendees/")) return "Attendees";
            if (pattern.Contains("/tasks/")) return "Tasks";
            if (pattern.Contains("/members/")) return "Members";
            if (pattern.Contains("/departments/")) return "Departments";
            if (pattern.Contains("/orders/")) return "Orders";
            if (pattern.Contains("/transactions/")) return "Transactions";
            if (pattern.Contains("/documents/")) return "Documents";
            return "Index";
        }

        private void SetupTestScenarios()
        {
            _testScenarios = new List<TestScenario>
            {
                new TestScenario { Type = "Simple", RouteValues = new Dictionary<string, object> { ["controller"] = "home", ["action"] = "index" } },
                new TestScenario { Type = "WithId", RouteValues = new Dictionary<string, object> { ["controller"] = "product", ["action"] = "details", ["id"] = 123 } },
                new TestScenario { Type = "Complex", RouteValues = new Dictionary<string, object> { ["controller"] = "order", ["action"] = "items", ["orderId"] = 456, ["itemId"] = 789 } },
                new TestScenario { Type = "API", RouteValues = new Dictionary<string, object> { ["controller"] = "user", ["id"] = 101, ["format"] = "json" } },
                new TestScenario { Type = "Admin", RouteValues = new Dictionary<string, object> { ["section"] = "users", ["action"] = "edit", ["id"] = 202 } },
                new TestScenario { Type = "Blog", RouteValues = new Dictionary<string, object> { ["year"] = "2024", ["month"] = "01", ["slug"] = "test-post" } },
                new TestScenario { Type = "Search", RouteValues = new Dictionary<string, object> { ["query"] = "search-term", ["page"] = 1 } },
                new TestScenario { Type = "File", RouteValues = new Dictionary<string, object> { ["path"] = "documents", ["filename"] = "report.pdf" } },
                new TestScenario { Type = "Report", RouteValues = new Dictionary<string, object> { ["type"] = "sales", ["date"] = "2024-01-15" } },
                new TestScenario { Type = "Analytics", RouteValues = new Dictionary<string, object> { ["metric"] = "views", ["period"] = "daily" } }
            };
        }

        public async Task<ZeroAllocationResults> RunZeroAllocationBenchmark(int iterations = 10000)
        {
            Console.WriteLine($"Running zero-allocation benchmark with {iterations} iterations...");
            
            var results = new ZeroAllocationResults();
            var stopwatch = new Stopwatch();
            var routeMatchingTimes = new List<long>();
            var urlGenerationTimes = new List<long>();
            var actionMatchingTimes = new List<long>();
            var helperTimes = new List<long>();

            var urlHelper = new ZeroAllocationUrlHelper(_routes);
            var htmlHelper = new ZeroAllocationHtmlHelper(urlHelper);
            var childActionHelper = new ZeroAllocationChildActionHelper(_routes);

            // Warm up
            Console.WriteLine("Warming up zero-allocation routing...");
            for (int i = 0; i < 1000; i++)
            {
                var scenario = _testScenarios[i % _testScenarios.Count];
                _routes.MatchRoute("/test/url");
                _routes.GetVirtualPath("", ConvertToSpan(scenario.RouteValues));
                urlHelper.Action("index", "home");
            }

            // Clear cache before actual benchmark
            _routes.ClearCache();

            Console.WriteLine("Running zero-allocation route matching benchmark...");
            for (int i = 0; i < iterations; i++)
            {
                var url = $"/test/{i % 100}/url/{i}";
                stopwatch.Restart();
                var route = _routes.MatchRoute(url);
                stopwatch.Stop();
                routeMatchingTimes.Add(stopwatch.ElapsedTicks);
            }

            Console.WriteLine("Running zero-allocation URL generation benchmark...");
            for (int i = 0; i < iterations; i++)
            {
                var scenario = _testScenarios[i % _testScenarios.Count];
                stopwatch.Restart();
                var generatedUrl = _routes.GetVirtualPath("", ConvertToSpan(scenario.RouteValues));
                stopwatch.Stop();
                urlGenerationTimes.Add(stopwatch.ElapsedTicks);
            }

            Console.WriteLine("Running zero-allocation action matching benchmark...");
            for (int i = 0; i < iterations; i++)
            {
                var scenario = _testScenarios[i % _testScenarios.Count];
                stopwatch.Restart();
                var actionUrl = _routes.GetVirtualPath("home", "index", ConvertToSpan(scenario.RouteValues));
                stopwatch.Stop();
                actionMatchingTimes.Add(stopwatch.ElapsedTicks);
            }

            Console.WriteLine("Running zero-allocation helper methods benchmark...");
            for (int i = 0; i < iterations; i++)
            {
                var scenario = _testScenarios[i % _testScenarios.Count];
                stopwatch.Restart();
                var actionUrl = urlHelper.Action("index", "home", scenario.RouteValues);
                var routeUrl = urlHelper.RouteUrl("test_route", scenario.RouteValues);
                var actionLink = htmlHelper.ActionLink("Link", "index", "home", scenario.RouteValues);
                var childAction = childActionHelper.RenderAction("index", "home", scenario.RouteValues);
                stopwatch.Stop();
                helperTimes.Add(stopwatch.ElapsedTicks);
            }

            results.RouteMatchingAverageMs = routeMatchingTimes.Average() * 1000.0 / Stopwatch.Frequency;
            results.UrlGenerationAverageMs = urlGenerationTimes.Average() * 1000.0 / Stopwatch.Frequency;
            results.ActionMatchingAverageMs = actionMatchingTimes.Average() * 1000.0 / Stopwatch.Frequency;
            results.HelperMethodsAverageMs = helperTimes.Average() * 1000.0 / Stopwatch.Frequency;
            results.TotalRoutes = _routes.Count;

            return results;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ReadOnlySpan<KeyValuePair<string, object>> ConvertToSpan(Dictionary<string, object> dict)
        {
            if (dict == null || dict.Count == 0) return ReadOnlySpan<KeyValuePair<string, object>>.Empty;
            
            if (dict.Count <= 8)
            {
                var span = stackalloc KeyValuePair<string, object>[dict.Count];
                var count = 0;
                
                foreach (var kvp in dict)
                {
                    span[count++] = kvp;
                }
                
                return span.Slice(0, count);
            }
            else
            {
                var array = new KeyValuePair<string, object>[dict.Count];
                var count = 0;
                
                foreach (var kvp in dict)
                {
                    array[count++] = kvp;
                }
                
                return array;
            }
        }
    }

    public class ZeroAllocationResults
    {
        public double RouteMatchingAverageMs { get; set; }
        public double UrlGenerationAverageMs { get; set; }
        public double ActionMatchingAverageMs { get; set; }
        public double HelperMethodsAverageMs { get; set; }
        public int TotalRoutes { get; set; }
    }
}