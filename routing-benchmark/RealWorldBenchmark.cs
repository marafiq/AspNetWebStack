using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;

namespace RoutingBenchmark
{
    public class RealWorldRoute
    {
        public string Pattern { get; set; } = "";
        public Regex CompiledRegex { get; set; } = null!;
        public Dictionary<string, string> Defaults { get; set; } = new Dictionary<string, string>();
        public string Controller { get; set; } = "";
        public string Action { get; set; } = "";
        public int Priority { get; set; }
        public string Category { get; set; } = "";
    }

    public class RealWorldRouteCollection
    {
        private readonly List<RealWorldRoute> _routes = new List<RealWorldRoute>();
        private readonly ConcurrentDictionary<string, RouteMatch> _routeCache = new ConcurrentDictionary<string, RouteMatch>();
        private readonly object _lockObject = new object();

        public void AddRoute(RealWorldRoute route)
        {
            lock (_lockObject)
            {
                _routes.Add(route);
                // Sort by priority (highest first)
                _routes.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            }
        }

        public RouteMatch MatchRoute(string url)
        {
            // Check cache first
            if (_routeCache.TryGetValue(url, out var cachedMatch))
            {
                return cachedMatch;
            }

            // Find matching route
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

                    // Extract parameters from regex groups
                    foreach (Group group in match.Groups)
                    {
                        if (group.Name != "0" && group.Success) // Skip the full match group
                        {
                            routeMatch.Parameters[group.Name] = group.Value;
                        }
                    }

                    // Add defaults
                    foreach (var defaultValue in route.Defaults)
                    {
                        if (!routeMatch.Parameters.ContainsKey(defaultValue.Key))
                        {
                            routeMatch.Parameters[defaultValue.Key] = defaultValue.Value;
                        }
                    }

                    // Cache the result
                    _routeCache.TryAdd(url, routeMatch);
                    return routeMatch;
                }
            }

            // No match found
            var noMatch = new RouteMatch { IsMatch = false };
            _routeCache.TryAdd(url, noMatch);
            return noMatch;
        }

        public void ClearCache()
        {
            _routeCache.Clear();
        }

        public int Count => _routes.Count;
        public List<RealWorldRoute> Routes => _routes;
    }

    public class RealWorldRoutingBenchmark
    {
        private RealWorldRouteCollection _routes;
        private List<string> _testUrls;
        private readonly Random _random = new Random(42);

        // Real-world route patterns
        private readonly string[] _controllers = {
            "Home", "Account", "User", "Product", "Order", "Cart", "Admin", "Api", "Blog", "News",
            "Category", "Search", "Profile", "Settings", "Payment", "Shipping", "Review", "Comment",
            "File", "Image", "Video", "Document", "Report", "Analytics", "Dashboard", "Notification",
            "Message", "Chat", "Forum", "Event", "Calendar", "Task", "Project", "Team", "Company",
            "Customer", "Supplier", "Inventory", "Warehouse", "Logistics", "Finance", "HR", "Legal"
        };

        private readonly string[] _actions = {
            "Index", "Details", "Create", "Edit", "Delete", "List", "Search", "Filter", "Sort",
            "Export", "Import", "Download", "Upload", "Preview", "Print", "Share", "Like", "Follow",
            "Subscribe", "Unsubscribe", "Verify", "Confirm", "Cancel", "Refund", "Return", "Track",
            "Update", "Save", "Publish", "Draft", "Archive", "Restore", "Move", "Copy", "Clone",
            "Merge", "Split", "Convert", "Transform", "Validate", "Authenticate", "Authorize"
        };

        private readonly string[] _areas = {
            "admin", "api", "mobile", "desktop", "public", "private", "internal", "external",
            "v1", "v2", "v3", "beta", "alpha", "staging", "dev", "test", "prod", "live",
            "secure", "portal", "dashboard", "console", "panel", "interface", "service"
        };

        public RealWorldRoutingBenchmark()
        {
            _routes = new RealWorldRouteCollection();
            _testUrls = new List<string>();
        }

        public void SetupRealWorldRoutes(int routeCount = 5000)
        {
            Console.WriteLine($"Setting up {routeCount:N0} real-world routes...");
            
            var routePatterns = GenerateRealWorldRoutePatterns(routeCount);
            
            for (int i = 0; i < routeCount; i++)
            {
                var pattern = routePatterns[i];
                var route = new RealWorldRoute
                {
                    Pattern = pattern,
                    CompiledRegex = CreateRegexFromPattern(pattern),
                    Defaults = new Dictionary<string, string>(),
                    Controller = _controllers[i % _controllers.Length],
                    Action = _actions[i % _actions.Length],
                    Priority = i < 100 ? 100 - i : 1,
                    Category = GetRouteCategory(pattern)
                };

                _routes.AddRoute(route);
                _testUrls.Add(GenerateTestUrl(pattern));
            }

            Console.WriteLine($"Setup complete: {_routes.Count} real-world routes");
            Console.WriteLine($"Route categories: {string.Join(", ", _routes.Routes.Select(r => r.Category).Distinct())}");
        }

        private string[] GenerateRealWorldRoutePatterns(int count)
        {
            var patterns = new List<string>();
            var patternTemplates = new[]
            {
                // Simple patterns
                "{controller}/{action}",
                "{controller}/{action}/{id}",
                "{controller}/{action}/{id}/{slug}",
                
                // Area-based patterns
                "{area}/{controller}/{action}",
                "{area}/{controller}/{action}/{id}",
                "{area}/v{version}/{controller}/{action}",
                
                // RESTful patterns
                "api/{controller}",
                "api/{controller}/{id}",
                "api/{controller}/{id}/{action}",
                "api/v{version}/{controller}",
                
                // Complex patterns
                "{controller}/{action}/{id}/{slug}/{category}",
                "{area}/{controller}/{action}/{id}/{param1}/{param2}",
                "api/{controller}/{action}/{id}/{format}",
                
                // Nested patterns
                "{controller}/{action}/{id}/{subaction}",
                "{area}/{controller}/{action}/{id}/{subcontroller}/{subaction}",
                
                // Parameter-heavy patterns
                "{controller}/{action}/{id}/{param1}/{param2}/{param3}",
                "api/{controller}/{action}/{id}/{format}/{version}",
                
                // Custom patterns
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
                "hr/{employeeId}/documents/{docId}"
            };

            for (int i = 0; i < count; i++)
            {
                var template = patternTemplates[i % patternTemplates.Length];
                var pattern = template
                    .Replace("{controller}", _controllers[i % _controllers.Length].ToLower())
                    .Replace("{action}", _actions[i % _actions.Length].ToLower())
                    .Replace("{area}", _areas[i % _areas.Length])
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
                    .Replace("{month}", "{month}")
                    .Replace("{subaction}", "{subaction}");

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

        private string GenerateTestUrl(string pattern)
        {
            return pattern
                .Replace("{id}", "123")
                .Replace("{slug}", "test-slug")
                .Replace("{category}", "electronics")
                .Replace("{param1}", "param1")
                .Replace("{param2}", "param2")
                .Replace("{param3}", "param3")
                .Replace("{version}", "1")
                .Replace("{format}", "json")
                .Replace("{subaction}", "details")
                .Replace("{subcontroller}", "sub")
                .Replace("{query}", "search-term")
                .Replace("{page}", "1")
                .Replace("{path}", "path/to/file")
                .Replace("{filename}", "document.pdf")
                .Replace("{type}", "report")
                .Replace("{date}", "2024-01-15")
                .Replace("{metric}", "views")
                .Replace("{period}", "daily")
                .Replace("{threadId}", "456")
                .Replace("{messageId}", "789")
                .Replace("{eventId}", "101")
                .Replace("{attendeeId}", "202")
                .Replace("{projectId}", "303")
                .Replace("{taskId}", "404")
                .Replace("{teamId}", "505")
                .Replace("{memberId}", "606")
                .Replace("{companyId}", "707")
                .Replace("{deptId}", "808")
                .Replace("{customerId}", "909")
                .Replace("{orderId}", "111")
                .Replace("{warehouseId}", "222")
                .Replace("{itemId}", "333")
                .Replace("{accountId}", "444")
                .Replace("{txnId}", "555")
                .Replace("{employeeId}", "666")
                .Replace("{docId}", "777")
                .Replace("{username}", "john_doe")
                .Replace("{year}", "2024")
                .Replace("{month}", "01");
        }

        private string GetRouteCategory(string pattern)
        {
            if (pattern.StartsWith("api/")) return "API";
            if (pattern.StartsWith("admin/")) return "Admin";
            if (pattern.StartsWith("products/")) return "E-commerce";
            if (pattern.StartsWith("users/")) return "User Management";
            if (pattern.StartsWith("orders/")) return "Order Management";
            if (pattern.StartsWith("blog/")) return "Content";
            if (pattern.StartsWith("search/")) return "Search";
            if (pattern.StartsWith("files/")) return "File Management";
            if (pattern.StartsWith("reports/")) return "Reporting";
            if (pattern.StartsWith("analytics/")) return "Analytics";
            if (pattern.StartsWith("notifications/")) return "Notifications";
            if (pattern.StartsWith("messages/")) return "Messaging";
            if (pattern.StartsWith("events/")) return "Events";
            if (pattern.StartsWith("projects/")) return "Project Management";
            if (pattern.StartsWith("teams/")) return "Team Management";
            if (pattern.StartsWith("companies/")) return "Company Management";
            if (pattern.StartsWith("customers/")) return "Customer Management";
            if (pattern.StartsWith("inventory/")) return "Inventory";
            if (pattern.StartsWith("finance/")) return "Finance";
            if (pattern.StartsWith("hr/")) return "HR";
            return "General";
        }

        public async Task<BenchmarkResults> RunRealWorldBenchmark(int iterations = 10000)
        {
            Console.WriteLine($"Running real-world benchmark with {iterations} iterations...");
            
            var results = new BenchmarkResults();
            var stopwatch = new Stopwatch();
            var times = new List<long>();

            // Warm up
            Console.WriteLine("Warming up real-world routing...");
            for (int i = 0; i < 1000; i++)
            {
                var url = _testUrls[i % _testUrls.Count];
                _routes.MatchRoute(url);
            }

            // Clear cache before actual benchmark
            _routes.ClearCache();

            Console.WriteLine("Running real-world route matching benchmark...");
            for (int i = 0; i < iterations; i++)
            {
                var url = _testUrls[i % _testUrls.Count];
                stopwatch.Restart();
                var route = _routes.MatchRoute(url);
                stopwatch.Stop();
                times.Add(stopwatch.ElapsedTicks);
            }

            results.StandardAverageTimeMs = times.Average() * 1000.0 / Stopwatch.Frequency;
            results.StandardMinTimeMs = times.Min() * 1000.0 / Stopwatch.Frequency;
            results.StandardMaxTimeMs = times.Max() * 1000.0 / Stopwatch.Frequency;
            results.StandardP95TimeMs = CalculatePercentile(times, 95) * 1000.0 / Stopwatch.Frequency;
            results.TotalRoutes = _routes.Count;

            return results;
        }

        private double CalculatePercentile(List<long> values, int percentile)
        {
            var sorted = values.OrderBy(x => x).ToList();
            var index = (int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1;
            return sorted[index];
        }
    }
}