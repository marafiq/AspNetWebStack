using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Text;

namespace RoutingBenchmark
{
    public class UrlGenerationRoute
    {
        public string Pattern { get; set; } = "";
        public Regex CompiledRegex { get; set; } = null!;
        public Dictionary<string, string> Defaults { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> Constraints { get; set; } = new Dictionary<string, string>();
        public string Controller { get; set; } = "";
        public string Action { get; set; } = "";
        public int Priority { get; set; }
        public string Name { get; set; } = "";
        public List<string> ParameterNames { get; set; } = new List<string>();
        public List<string> StaticSegments { get; set; } = new List<string>();
    }

    public class UrlGenerationRouteCollection
    {
        private readonly List<UrlGenerationRoute> _routes = new List<UrlGenerationRoute>();
        private readonly ConcurrentDictionary<string, RouteMatch> _routeCache = new ConcurrentDictionary<string, RouteMatch>();
        private readonly ConcurrentDictionary<string, string> _urlCache = new ConcurrentDictionary<string, string>();
        private readonly object _lockObject = new object();

        public void AddRoute(UrlGenerationRoute route)
        {
            lock (_lockObject)
            {
                _routes.Add(route);
                _routes.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            }
        }

        public RouteMatch MatchRoute(string url)
        {
            if (_routeCache.TryGetValue(url, out var cachedMatch))
            {
                return cachedMatch;
            }

            foreach (var route in _routes)
            {
                var match = route.CompiledRegex.Match(url);
                if (match.Success)
                {
                    var routeMatch = new RouteMatch
                    {
                        Controller = route.Controller,
                        Action = route.Action,
                        IsMatch = true
                    };

                    foreach (Group group in match.Groups)
                    {
                        if (group.Name != "0" && group.Success)
                        {
                            routeMatch.Parameters[group.Name] = group.Value;
                        }
                    }

                    foreach (var defaultValue in route.Defaults)
                    {
                        if (!routeMatch.Parameters.ContainsKey(defaultValue.Key))
                        {
                            routeMatch.Parameters[defaultValue.Key] = defaultValue.Value;
                        }
                    }

                    _routeCache.TryAdd(url, routeMatch);
                    return routeMatch;
                }
            }

            var noMatch = new RouteMatch { IsMatch = false };
            _routeCache.TryAdd(url, noMatch);
            return noMatch;
        }

        public string? GetVirtualPath(string routeName, Dictionary<string, object> routeValues)
        {
            var cacheKey = $"{routeName}:{string.Join("|", routeValues.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"))}";
            
            if (_urlCache.TryGetValue(cacheKey, out var cachedUrl))
            {
                return cachedUrl;
            }

            var route = _routes.FirstOrDefault(r => r.Name == routeName);
            if (route == null) return null;

            var url = GenerateUrl(route, routeValues);
            if (url != null)
            {
                _urlCache.TryAdd(cacheKey, url);
            }

            return url;
        }

        private string? GenerateUrl(UrlGenerationRoute route, Dictionary<string, object> routeValues)
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
        }

        public void ClearCache()
        {
            _routeCache.Clear();
            _urlCache.Clear();
        }

        public int Count => _routes.Count;
    }

    // IL Replacement Helper Classes
    public class UrlHelper
    {
        private readonly UrlGenerationRouteCollection _routes;

        public UrlHelper(UrlGenerationRouteCollection routes)
        {
            _routes = routes;
        }

        public string? Action(string actionName, string controllerName = "", object routeValues = null)
        {
            var values = ConvertToDictionary(routeValues);
            values["action"] = actionName;
            if (!string.IsNullOrEmpty(controllerName))
                values["controller"] = controllerName;

            return _routes.GetVirtualPath("", values);
        }

        public string? RouteUrl(string routeName, object routeValues = null)
        {
            var values = ConvertToDictionary(routeValues);
            return _routes.GetVirtualPath(routeName, values);
        }

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

    public class HtmlHelper
    {
        private readonly UrlHelper _urlHelper;

        public HtmlHelper(UrlHelper urlHelper)
        {
            _urlHelper = urlHelper;
        }

        public string ActionLink(string linkText, string actionName, string controllerName = "", object routeValues = null)
        {
            var url = _urlHelper.Action(actionName, controllerName, routeValues);
            return $"<a href=\"{url}\">{linkText}</a>";
        }

        public string RouteLink(string linkText, string routeName, object routeValues = null)
        {
            var url = _urlHelper.RouteUrl(routeName, routeValues);
            return $"<a href=\"{url}\">{linkText}</a>";
        }
    }

    public class ChildActionHelper
    {
        private readonly UrlGenerationRouteCollection _routes;

        public ChildActionHelper(UrlGenerationRouteCollection routes)
        {
            _routes = routes;
        }

        public string RenderAction(string actionName, string controllerName = "", object routeValues = null)
        {
            var values = ConvertToDictionary(routeValues);
            values["action"] = actionName;
            values["controller"] = controllerName;

            // Simulate child action rendering
            var url = _routes.GetVirtualPath("", values);
            return $"<!-- Child Action: {url} -->";
        }

        public string RenderPartial(string partialViewName, object model = null)
        {
            return $"<!-- Partial View: {partialViewName} -->";
        }

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

    public class UrlGenerationBenchmark
    {
        private UrlGenerationRouteCollection _routes;
        private List<TestScenario> _testScenarios;
        private readonly Random _random = new Random(42);

        public UrlGenerationBenchmark()
        {
            _routes = new UrlGenerationRouteCollection();
            _testScenarios = new List<TestScenario>();
        }

        public void SetupUrlGenerationRoutes(int routeCount = 5000)
        {
            Console.WriteLine($"Setting up {routeCount:N0} URL generation routes...");
            
            var routePatterns = GenerateUrlGenerationPatterns(routeCount);
            
            for (int i = 0; i < routeCount; i++)
            {
                var pattern = routePatterns[i];
                var route = new UrlGenerationRoute
                {
                    Pattern = pattern,
                    CompiledRegex = CreateRegexFromPattern(pattern),
                    Defaults = new Dictionary<string, string>(),
                    Constraints = new Dictionary<string, string>(),
                    Controller = GetControllerFromPattern(pattern),
                    Action = GetActionFromPattern(pattern),
                    Priority = i < 100 ? 100 - i : 1,
                    Name = $"route_{i}",
                    ParameterNames = ExtractParameterNames(pattern),
                    StaticSegments = ExtractStaticSegments(pattern)
                };

                _routes.AddRoute(route);
            }

            SetupTestScenarios();
            Console.WriteLine($"Setup complete: {_routes.Count} URL generation routes");
        }

        private string[] GenerateUrlGenerationPatterns(int count)
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

        private List<string> ExtractParameterNames(string pattern)
        {
            var parameters = new List<string>();
            var matches = Regex.Matches(pattern, @"\{([^}]+)\}");
            foreach (Match match in matches)
            {
                parameters.Add(match.Groups[1].Value);
            }
            return parameters;
        }

        private List<string> ExtractStaticSegments(string pattern)
        {
            var segments = new List<string>();
            var parts = pattern.Split('/');
            foreach (var part in parts)
            {
                if (!string.IsNullOrEmpty(part) && !part.StartsWith("{") && !part.EndsWith("}"))
                {
                    segments.Add(part);
                }
            }
            return segments;
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

        public async Task<UrlGenerationResults> RunUrlGenerationBenchmark(int iterations = 10000)
        {
            Console.WriteLine($"Running URL generation benchmark with {iterations} iterations...");
            
            var results = new UrlGenerationResults();
            var stopwatch = new Stopwatch();
            var routeMatchingTimes = new List<long>();
            var urlGenerationTimes = new List<long>();
            var helperTimes = new List<long>();

            var urlHelper = new UrlHelper(_routes);
            var htmlHelper = new HtmlHelper(urlHelper);
            var childActionHelper = new ChildActionHelper(_routes);

            // Warm up
            Console.WriteLine("Warming up URL generation...");
            for (int i = 0; i < 1000; i++)
            {
                var scenario = _testScenarios[i % _testScenarios.Count];
                _routes.MatchRoute("/test/url");
                _routes.GetVirtualPath("", scenario.RouteValues);
                urlHelper.Action("index", "home");
            }

            // Clear cache before actual benchmark
            _routes.ClearCache();

            Console.WriteLine("Running route matching benchmark...");
            for (int i = 0; i < iterations; i++)
            {
                var url = $"/test/{i % 100}/url/{i}";
                stopwatch.Restart();
                var route = _routes.MatchRoute(url);
                stopwatch.Stop();
                routeMatchingTimes.Add(stopwatch.ElapsedTicks);
            }

            Console.WriteLine("Running URL generation benchmark...");
            for (int i = 0; i < iterations; i++)
            {
                var scenario = _testScenarios[i % _testScenarios.Count];
                stopwatch.Restart();
                var generatedUrl = _routes.GetVirtualPath("", scenario.RouteValues);
                stopwatch.Stop();
                urlGenerationTimes.Add(stopwatch.ElapsedTicks);
            }

            Console.WriteLine("Running helper methods benchmark...");
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
            results.HelperMethodsAverageMs = helperTimes.Average() * 1000.0 / Stopwatch.Frequency;
            results.TotalRoutes = _routes.Count;

            return results;
        }
    }

    public class TestScenario
    {
        public string Type { get; set; } = "";
        public Dictionary<string, object> RouteValues { get; set; } = new Dictionary<string, object>();
    }

    public class UrlGenerationResults
    {
        public double RouteMatchingAverageMs { get; set; }
        public double UrlGenerationAverageMs { get; set; }
        public double HelperMethodsAverageMs { get; set; }
        public int TotalRoutes { get; set; }
    }
}