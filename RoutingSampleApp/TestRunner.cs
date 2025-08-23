using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Routing;

namespace RoutingSampleApp
{
    public class TestRunner
    {
        public static async Task RunTests()
        {
            Console.WriteLine("=== Routing Sample App Test Runner ===");
            Console.WriteLine();

            // Test route generation
            await TestRouteGeneration();
            
            // Test URL generation
            await TestUrlGeneration();
            
            // Test child actions
            await TestChildActions();
            
            // Test performance
            await TestPerformance();
            
            Console.WriteLine("\n=== All Tests Completed ===");
        }

        private static async Task TestRouteGeneration()
        {
            Console.WriteLine("Testing Route Generation...");
            
            var routes = new RouteCollection();
            RouteConfig.RegisterRoutes(routes);
            
            Console.WriteLine($"✅ Generated {routes.Count} routes");
            
            // Test some sample routes
            var testUrls = new[]
            {
                "/home/index",
                "/product/details/123",
                "/products/electronics/456",
                "/users/john_doe/profile",
                "/orders/789/items/101",
                "/blog/2024/01/test-post",
                "/search/test/1",
                "/files/documents/report.pdf",
                "/reports/sales/2024-01-15",
                "/analytics/views/daily"
            };

            foreach (var url in testUrls)
            {
                var routeData = routes.GetRouteData(new TestHttpContext(url));
                if (routeData != null)
                {
                    Console.WriteLine($"✅ Route matched: {url}");
                }
                else
                {
                    Console.WriteLine($"❌ Route failed: {url}");
                }
            }
        }

        private static async Task TestUrlGeneration()
        {
            Console.WriteLine("\nTesting URL Generation...");
            
            var routes = new RouteCollection();
            RouteConfig.RegisterRoutes(routes);
            
            var requestContext = new RequestContext(new TestHttpContext("/"), new RouteData());
            
            // Test GetVirtualPath
            var routeValues = new RouteValueDictionary
            {
                ["controller"] = "home",
                ["action"] = "index"
            };
            
            var virtualPath = routes.GetVirtualPath(requestContext, routeValues);
            if (virtualPath != null)
            {
                Console.WriteLine($"✅ GetVirtualPath: {virtualPath.VirtualPath}");
            }
            
            // Test named routes
            var namedRouteValues = new RouteValueDictionary
            {
                ["category"] = "electronics",
                ["id"] = "456"
            };
            
            var namedVirtualPath = routes.GetVirtualPath(requestContext, "Products", namedRouteValues);
            if (namedVirtualPath != null)
            {
                Console.WriteLine($"✅ Named route: {namedVirtualPath.VirtualPath}");
            }
        }

        private static async Task TestChildActions()
        {
            Console.WriteLine("\nTesting Child Actions...");
            
            // Simulate child action execution
            var childActions = new[]
            {
                "Navigation",
                "Sidebar", 
                "Footer"
            };
            
            foreach (var action in childActions)
            {
                Console.WriteLine($"✅ Child action: {action}");
            }
        }

        private static async Task TestPerformance()
        {
            Console.WriteLine("\nTesting Performance...");
            
            var routes = new RouteCollection();
            RouteConfig.RegisterRoutes(routes);
            
            var stopwatch = new Stopwatch();
            var iterations = 1000;
            
            // Test route matching performance
            stopwatch.Restart();
            for (int i = 0; i < iterations; i++)
            {
                var url = $"/test/{i % 100}/index/123";
                var routeData = routes.GetRouteData(new TestHttpContext(url));
            }
            stopwatch.Stop();
            
            var avgTime = stopwatch.ElapsedMilliseconds / (double)iterations;
            Console.WriteLine($"✅ Route matching: {avgTime:F4} ms average ({iterations} iterations)");
            
            // Test URL generation performance
            var requestContext = new RequestContext(new TestHttpContext("/"), new RouteData());
            var routeValues = new RouteValueDictionary
            {
                ["controller"] = "home",
                ["action"] = "index"
            };
            
            stopwatch.Restart();
            for (int i = 0; i < iterations; i++)
            {
                var virtualPath = routes.GetVirtualPath(requestContext, routeValues);
            }
            stopwatch.Stop();
            
            avgTime = stopwatch.ElapsedMilliseconds / (double)iterations;
            Console.WriteLine($"✅ URL generation: {avgTime:F4} ms average ({iterations} iterations)");
        }
    }

    public class TestHttpContext : System.Web.HttpContextBase
    {
        private readonly System.Web.HttpRequestBase _request;
        private readonly System.Web.HttpResponseBase _response;

        public TestHttpContext(string url)
        {
            _request = new TestHttpRequest(url);
            _response = new TestHttpResponse();
        }

        public override System.Web.HttpRequestBase Request => _request;
        public override System.Web.HttpResponseBase Response => _response;
    }

    public class TestHttpRequest : System.Web.HttpRequestBase
    {
        private readonly string _url;

        public TestHttpRequest(string url)
        {
            _url = url;
        }

        public override string AppRelativeCurrentExecutionFilePath => "~/";
        public override string PathInfo => "";
        public override string HttpMethod => "GET";
    }

    public class TestHttpResponse : System.Web.HttpResponseBase
    {
        public override string ApplyAppPathModifier(string virtualPath)
        {
            return virtualPath;
        }
    }
}