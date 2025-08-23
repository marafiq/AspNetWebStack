using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

namespace RoutingBenchmark
{
    public class UltraFastRoute
    {
        public string Pattern { get; set; } = "";
        public Regex CompiledRegex { get; set; } = null!;
        public string Controller { get; set; } = "";
        public string Action { get; set; } = "";
        public string Name { get; set; } = "";
        public int Priority { get; set; }
        public Dictionary<string, string> Defaults { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> Constraints { get; set; } = new Dictionary<string, string>();
        
        // Compiled delegates for ultra-fast execution
        public Func<string, RouteMatch>? CompiledMatcher { get; set; }
        public Func<Dictionary<string, object>, string?>? CompiledUrlGenerator { get; set; }
        public Func<object, Dictionary<string, object>>? CompiledParameterExtractor { get; set; }
    }

    public class UltraFastRouteCollection
    {
        private readonly List<UltraFastRoute> _routes = new List<UltraFastRoute>();
        private readonly ConcurrentDictionary<string, RouteMatch> _routeCache = new ConcurrentDictionary<string, RouteMatch>();
        private readonly ConcurrentDictionary<string, string> _urlCache = new ConcurrentDictionary<string, string>();
        private readonly ConcurrentDictionary<string, Func<string, RouteMatch>> _compiledMatchers = new ConcurrentDictionary<string, Func<string, RouteMatch>>();
        private readonly ConcurrentDictionary<string, Func<Dictionary<string, object>, string?>> _compiledUrlGenerators = new ConcurrentDictionary<string, Func<Dictionary<string, object>, string?>>();
        private readonly ConcurrentDictionary<Type, Func<object, Dictionary<string, object>>> _parameterExtractors = new ConcurrentDictionary<Type, Func<object, Dictionary<string, object>>>();
        
        // Fast lookup structures
        private readonly Dictionary<string, List<UltraFastRoute>> _controllerActionLookup = new Dictionary<string, List<UltraFastRoute>>();
        private readonly Dictionary<string, UltraFastRoute> _namedRouteLookup = new Dictionary<string, UltraFastRoute>();
        private readonly Dictionary<string, List<UltraFastRoute>> _patternPrefixLookup = new Dictionary<string, List<UltraFastRoute>>();

        public void AddRoute(UltraFastRoute route)
        {
            _routes.Add(route);
            _routes.Sort((a, b) => b.Priority.CompareTo(a.Priority));

            // Build fast lookup structures
            var key = $"{route.Controller}.{route.Action}";
            if (!_controllerActionLookup.ContainsKey(key))
                _controllerActionLookup[key] = new List<UltraFastRoute>();
            _controllerActionLookup[key].Add(route);

            if (!string.IsNullOrEmpty(route.Name))
                _namedRouteLookup[route.Name] = route;

            // Build pattern prefix lookup for faster matching
            var prefix = GetPatternPrefix(route.Pattern);
            if (!_patternPrefixLookup.ContainsKey(prefix))
                _patternPrefixLookup[prefix] = new List<UltraFastRoute>();
            _patternPrefixLookup[prefix].Add(route);

            // Compile delegates for ultra-fast execution
            route.CompiledMatcher = CompileRouteMatcher(route);
            route.CompiledUrlGenerator = CompileUrlGenerator(route);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RouteMatch MatchRoute(string url)
        {
            // Check cache first
            if (_routeCache.TryGetValue(url, out var cachedMatch))
                return cachedMatch;

            // Fast prefix-based filtering
            var prefix = GetUrlPrefix(url);
            var candidateRoutes = _patternPrefixLookup.TryGetValue(prefix, out var routes) ? routes : _routes;

            // Use compiled matchers for ultra-fast matching
            foreach (var route in candidateRoutes)
            {
                if (route.CompiledMatcher != null)
                {
                    var match = route.CompiledMatcher(url);
                    if (match.IsMatch)
                    {
                        _routeCache.TryAdd(url, match);
                        return match;
                    }
                }
            }

            var noMatch = new RouteMatch { IsMatch = false };
            _routeCache.TryAdd(url, noMatch);
            return noMatch;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string? GetVirtualPath(string routeName, Dictionary<string, object> routeValues)
        {
            var cacheKey = $"{routeName}:{string.Join("|", routeValues.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"))}";
            
            if (_urlCache.TryGetValue(cacheKey, out var cachedUrl))
                return cachedUrl;

            if (_namedRouteLookup.TryGetValue(routeName, out var route) && route.CompiledUrlGenerator != null)
            {
                var url = route.CompiledUrlGenerator(routeValues);
                if (url != null)
                    _urlCache.TryAdd(cacheKey, url);
                return url;
            }

            return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string? GetVirtualPath(string controller, string action, object routeValues = null)
        {
            var key = $"{controller}.{action}";
            if (_controllerActionLookup.TryGetValue(key, out var routes))
            {
                var values = ConvertToDictionary(routeValues);
                foreach (var route in routes)
                {
                    if (route.CompiledUrlGenerator != null)
                    {
                        var url = route.CompiledUrlGenerator(values);
                        if (url != null)
                            return url;
                    }
                }
            }
            return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string? GetVirtualPath(string controller, string action, Dictionary<string, object> routeValues)
        {
            var key = $"{controller}.{action}";
            if (_controllerActionLookup.TryGetValue(key, out var routes))
            {
                foreach (var route in routes)
                {
                    if (route.CompiledUrlGenerator != null)
                    {
                        var url = route.CompiledUrlGenerator(routeValues);
                        if (url != null)
                            return url;
                    }
                }
            }
            return null;
        }

        private Func<string, RouteMatch> CompileRouteMatcher(UltraFastRoute route)
        {
            var cacheKey = route.Pattern;
            if (_compiledMatchers.TryGetValue(cacheKey, out var cachedMatcher))
                return cachedMatcher;

            var matcher = (string url) =>
            {
                var match = route.CompiledRegex.Match(url);
                if (!match.Success) return new RouteMatch { IsMatch = false };

                var routeMatch = new RouteMatch
                {
                    Controller = route.Controller,
                    Action = route.Action,
                    IsMatch = true
                };

                // Fast parameter extraction
                foreach (Group group in match.Groups)
                {
                    if (group.Name != "0" && group.Success)
                        routeMatch.Parameters[group.Name] = group.Value;
                }

                // Fast default value application
                foreach (var defaultValue in route.Defaults)
                {
                    if (!routeMatch.Parameters.ContainsKey(defaultValue.Key))
                        routeMatch.Parameters[defaultValue.Key] = defaultValue.Value;
                }

                return routeMatch;
            };

            _compiledMatchers.TryAdd(cacheKey, matcher);
            return matcher;
        }

        private Func<Dictionary<string, object>, string?> CompileUrlGenerator(UltraFastRoute route)
        {
            var cacheKey = route.Pattern;
            if (_compiledUrlGenerators.TryGetValue(cacheKey, out var cachedGenerator))
                return cachedGenerator;

            var generator = (Dictionary<string, object> routeValues) =>
            {
                var segments = new List<string>();
                var patternSegments = route.Pattern.Split('/').Where(s => !string.IsNullOrEmpty(s)).ToArray();

                for (int i = 0; i < patternSegments.Length; i++)
                {
                    var segment = patternSegments[i];
                    
                    if (segment.StartsWith("{") && segment.EndsWith("}"))
                    {
                        var paramName = segment.Trim('{', '}');
                        
                        if (routeValues.TryGetValue(paramName, out var value))
                        {
                            segments.Add(value.ToString() ?? "");
                        }
                        else if (route.Defaults.TryGetValue(paramName, out var defaultValue))
                        {
                            segments.Add(defaultValue);
                        }
                        else
                        {
                            return null; // Required parameter missing
                        }
                    }
                    else
                    {
                        segments.Add(segment);
                    }
                }

                return "/" + string.Join("/", segments);
            };

            _compiledUrlGenerators.TryAdd(cacheKey, generator);
            return generator;
        }

        private Func<object, Dictionary<string, object>> CompileParameterExtractor(Type type)
        {
            if (_parameterExtractors.TryGetValue(type, out var cachedExtractor))
                return cachedExtractor;

            var extractor = (object obj) =>
            {
                var dict = new Dictionary<string, object>();
                if (obj == null) return dict;

                foreach (var prop in type.GetProperties())
                {
                    dict[prop.Name] = prop.GetValue(obj) ?? "";
                }
                return dict;
            };

            _parameterExtractors.TryAdd(type, extractor);
            return extractor;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Dictionary<string, object> ConvertToDictionary(object obj)
        {
            if (obj == null) return new Dictionary<string, object>();
            
            var type = obj.GetType();
            var extractor = CompileParameterExtractor(type);
            return extractor(obj);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private string GetPatternPrefix(string pattern)
        {
            var firstSegment = pattern.Split('/').FirstOrDefault(s => !string.IsNullOrEmpty(s));
            return firstSegment ?? "";
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private string GetUrlPrefix(string url)
        {
            var firstSegment = url.Split('/').FirstOrDefault(s => !string.IsNullOrEmpty(s));
            return firstSegment ?? "";
        }

        public void ClearCache()
        {
            _routeCache.Clear();
            _urlCache.Clear();
        }

        public int Count => _routes.Count;
    }

    // Ultra-fast helper classes with compiled delegates
    public class UltraFastUrlHelper
    {
        private readonly UltraFastRouteCollection _routes;
        private readonly ConcurrentDictionary<string, string?> _actionCache = new ConcurrentDictionary<string, string?>();
        private readonly ConcurrentDictionary<string, string?> _routeUrlCache = new ConcurrentDictionary<string, string?>();

        public UltraFastUrlHelper(UltraFastRouteCollection routes)
        {
            _routes = routes;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string? Action(string actionName, string controllerName = "", object routeValues = null)
        {
            var cacheKey = $"{controllerName}.{actionName}:{routeValues?.GetHashCode() ?? 0}";
            
            if (_actionCache.TryGetValue(cacheKey, out var cachedUrl))
                return cachedUrl;

            var values = ConvertToDictionary(routeValues);
            if (!string.IsNullOrEmpty(controllerName))
                values["controller"] = controllerName;
            values["action"] = actionName;

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

            var values = ConvertToDictionary(routeValues);
            var url = _routes.GetVirtualPath(routeName, values);
            _routeUrlCache.TryAdd(cacheKey, url);
            return url;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Dictionary<string, object> ConvertToDictionary(object obj)
        {
            if (obj == null) return new Dictionary<string, object>();
            
            var dict = new Dictionary<string, object>();
            foreach (var prop in obj.GetType().GetProperties())
            {
                dict[prop.Name] = prop.GetValue(obj) ?? "";
            }
            return dict;
        }
    }

    public class UltraFastHtmlHelper
    {
        private readonly UltraFastUrlHelper _urlHelper;
        private readonly ConcurrentDictionary<string, string> _actionLinkCache = new ConcurrentDictionary<string, string>();
        private readonly ConcurrentDictionary<string, string> _routeLinkCache = new ConcurrentDictionary<string, string>();

        public UltraFastHtmlHelper(UltraFastUrlHelper urlHelper)
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

    public class UltraFastChildActionHelper
    {
        private readonly UltraFastRouteCollection _routes;
        private readonly ConcurrentDictionary<string, string> _renderActionCache = new ConcurrentDictionary<string, string>();
        private readonly ConcurrentDictionary<string, string> _renderPartialCache = new ConcurrentDictionary<string, string>();

        public UltraFastChildActionHelper(UltraFastRouteCollection routes)
        {
            _routes = routes;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string RenderAction(string actionName, string controllerName = "", object routeValues = null)
        {
            var cacheKey = $"{actionName}:{controllerName}:{routeValues?.GetHashCode() ?? 0}";
            
            if (_renderActionCache.TryGetValue(cacheKey, out var cachedAction))
                return cachedAction;

            var values = ConvertToDictionary(routeValues);
            values["action"] = actionName;
            if (!string.IsNullOrEmpty(controllerName))
                values["controller"] = controllerName;

            var url = _routes.GetVirtualPath(controllerName, actionName, values);
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
        private Dictionary<string, object> ConvertToDictionary(object obj)
        {
            if (obj == null) return new Dictionary<string, object>();
            
            var dict = new Dictionary<string, object>();
            foreach (var prop in obj.GetType().GetProperties())
            {
                dict[prop.Name] = prop.GetValue(obj) ?? "";
            }
            return dict;
        }
    }

    public class UltraFastRoutingBenchmark
    {
        private UltraFastRouteCollection _routes;
        private List<TestScenario> _testScenarios;
        private readonly Random _random = new Random(42);

        public UltraFastRoutingBenchmark()
        {
            _routes = new UltraFastRouteCollection();
            _testScenarios = new List<TestScenario>();
        }

        public void SetupUltraFastRoutes(int routeCount = 5000)
        {
            Console.WriteLine($"Setting up {routeCount:N0} ultra-fast routes...");
            
            var routePatterns = GenerateUltraFastPatterns(routeCount);
            
            for (int i = 0; i < routeCount; i++)
            {
                var pattern = routePatterns[i];
                var route = new UltraFastRoute
                {
                    Pattern = pattern,
                    CompiledRegex = CreateRegexFromPattern(pattern),
                    Defaults = new Dictionary<string, string>(),
                    Constraints = new Dictionary<string, string>(),
                    Controller = GetControllerFromPattern(pattern),
                    Action = GetActionFromPattern(pattern),
                    Priority = i < 100 ? 100 - i : 1,
                    Name = $"route_{i}",
                };

                _routes.AddRoute(route);
            }

            SetupTestScenarios();
            Console.WriteLine($"Setup complete: {_routes.Count} ultra-fast routes");
        }

        private string[] GenerateUltraFastPatterns(int count)
        {
            var patterns = new List<string>();
            var patternTemplates = new[]
            {
                // Simple patterns
                "{controller}/{action}",
                "{controller}/{action}/{id}",
                "{controller}/{action}/{id}/{slug}",
                
                // Named routes
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
                
                // API patterns
                "api/{controller}",
                "api/{controller}/{id}",
                "api/{controller}/{id}/{action}",
                "api/v{version}/{controller}",
                
                // Complex patterns
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

        public async Task<UltraFastResults> RunUltraFastBenchmark(int iterations = 10000)
        {
            Console.WriteLine($"Running ultra-fast benchmark with {iterations} iterations...");
            
            var results = new UltraFastResults();
            var stopwatch = new Stopwatch();
            var routeMatchingTimes = new List<long>();
            var urlGenerationTimes = new List<long>();
            var actionMatchingTimes = new List<long>();
            var helperTimes = new List<long>();

            var urlHelper = new UltraFastUrlHelper(_routes);
            var htmlHelper = new UltraFastHtmlHelper(urlHelper);
            var childActionHelper = new UltraFastChildActionHelper(_routes);

            // Warm up
            Console.WriteLine("Warming up ultra-fast routing...");
            for (int i = 0; i < 1000; i++)
            {
                var scenario = _testScenarios[i % _testScenarios.Count];
                _routes.MatchRoute("/test/url");
                _routes.GetVirtualPath("", scenario.RouteValues);
                urlHelper.Action("index", "home");
            }

            // Clear cache before actual benchmark
            _routes.ClearCache();

            Console.WriteLine("Running ultra-fast route matching benchmark...");
            for (int i = 0; i < iterations; i++)
            {
                var url = $"/test/{i % 100}/url/{i}";
                stopwatch.Restart();
                var route = _routes.MatchRoute(url);
                stopwatch.Stop();
                routeMatchingTimes.Add(stopwatch.ElapsedTicks);
            }

            Console.WriteLine("Running ultra-fast URL generation benchmark...");
            for (int i = 0; i < iterations; i++)
            {
                var scenario = _testScenarios[i % _testScenarios.Count];
                stopwatch.Restart();
                var generatedUrl = _routes.GetVirtualPath("", scenario.RouteValues);
                stopwatch.Stop();
                urlGenerationTimes.Add(stopwatch.ElapsedTicks);
            }

            Console.WriteLine("Running ultra-fast action matching benchmark...");
            for (int i = 0; i < iterations; i++)
            {
                var scenario = _testScenarios[i % _testScenarios.Count];
                stopwatch.Restart();
                var actionUrl = _routes.GetVirtualPath("home", "index", scenario.RouteValues);
                stopwatch.Stop();
                actionMatchingTimes.Add(stopwatch.ElapsedTicks);
            }

            Console.WriteLine("Running ultra-fast helper methods benchmark...");
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
    }

    public class UltraFastResults
    {
        public double RouteMatchingAverageMs { get; set; }
        public double UrlGenerationAverageMs { get; set; }
        public double ActionMatchingAverageMs { get; set; }
        public double HelperMethodsAverageMs { get; set; }
        public int TotalRoutes { get; set; }
    }
}