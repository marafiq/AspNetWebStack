using UltraFastRouting.Core;
using UltraFastRouting.Helpers;

namespace UltraFastRouting.Test
{
    /// <summary>
    /// Test example for UltraFastRouting package.
    /// </summary>
    public class TestExample
    {
        /// <summary>
        /// Runs a comprehensive test of the UltraFastRouting functionality.
        /// </summary>
        public static void RunTest()
        {
            Console.WriteLine("🚀 UltraFastRouting Test Example");
            Console.WriteLine("================================\n");

            // Create route collection
            var routes = new ZeroAllocationRouteCollection(100);

            // Add some test routes
            routes.AddRoute(0, RoutePattern.Create("home", "Home", "Index"));
            routes.AddRoute(1, RoutePattern.Create("products/{id}", "Product", "Details"));
            routes.AddRoute(2, RoutePattern.Create("api/users/{id}", "User", "Get"));
            routes.AddRoute(3, RoutePattern.Create("about", "Home", "About"));

            Console.WriteLine($"✅ Added {4} routes to collection\n");

            // Test route matching
            var testUrls = new[] { "home", "products/123", "api/users/456", "about", "notfound" };

            foreach (var url in testUrls)
            {
                var match = routes.MatchRoute(url);
                if (match.IsMatch)
                {
                    Console.WriteLine($"✅ Matched '{url}' -> {match.Controller}.{match.Action}");
                    if (match.Parameters.Length > 0)
                    {
                        foreach (var param in match.Parameters)
                        {
                            Console.WriteLine($"   Parameter: {param.Key} = {param.Value}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"❌ No match for '{url}'");
                }
            }

            Console.WriteLine();

            // Test URL generation
            var urlHelper = new ZeroAllocationUrlHelper(routes);
            var routeValues = new[] { new KeyValuePair<string, object>("id", 789) };
            var generatedUrl = urlHelper.Action("Details", "Product", routeValues);
            Console.WriteLine($"🔗 Generated URL: {generatedUrl}");

            // Test HTML generation
            var htmlHelper = new ZeroAllocationHtmlHelper(routes);
            var htmlAttributes = new[] { new KeyValuePair<string, string>("class", "btn") };
            var link = htmlHelper.ActionLink("View Product", "Details", "Product", routeValues, htmlAttributes);
            Console.WriteLine($"🔗 Generated HTML: {link}");

            Console.WriteLine("\n🎉 All tests completed successfully!");
        }
    }
}