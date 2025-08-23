using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Web.Mvc;
using System.Web.Routing;
using System.Web.Http;
using System.Web.Http.Routing;
using System.Threading.Tasks;

namespace RoutingBenchmark
{
    public class BenchmarkController : Controller
    {
        public ActionResult Index() => Content("Hello World");
        public ActionResult About() => Content("About");
        public ActionResult Contact() => Content("Contact");
        public ActionResult Details(int id) => Content($"Details {id}");
        public ActionResult Edit(int id) => Content($"Edit {id}");
        public ActionResult Delete(int id) => Content($"Delete {id}");
        public ActionResult Create() => Content("Create");
        public ActionResult List() => Content("List");
        public ActionResult Search(string q) => Content($"Search: {q}");
        public ActionResult Category(string cat) => Content($"Category: {cat}");
    }

    public class ApiController : System.Web.Http.ApiController
    {
        public IHttpActionResult Get() => Ok("Hello API");
        public IHttpActionResult Get(int id) => Ok($"API {id}");
        public IHttpActionResult Post() => Ok("Posted");
        public IHttpActionResult Put(int id) => Ok($"Updated {id}");
        public IHttpActionResult Delete(int id) => Ok($"Deleted {id}");
    }

    public class RoutingBenchmark
    {
        private RouteCollection _mvcRoutes;
        private HttpRouteCollection _webApiRoutes;
        private List<string> _testUrls;
        private Random _random;

        public RoutingBenchmark()
        {
            _mvcRoutes = new RouteCollection();
            _webApiRoutes = new HttpRouteCollection();
            _testUrls = new List<string>();
            _random = new Random(42); // Fixed seed for consistent results
        }

        public void SetupRoutes()
        {
            Console.WriteLine("Setting up 5000 routes...");
            
            // Setup MVC routes
            for (int i = 0; i < 2500; i++)
            {
                var routeName = $"route_{i}";
                var route = new Route(
                    $"test/{i}/{{action}}/{{id}}",
                    new RouteValueDictionary(new { controller = "Benchmark" }),
                    new RouteValueDictionary(new { id = @"\d+" }),
                    new MvcRouteHandler()
                );
                _mvcRoutes.Add(routeName, route);
                _testUrls.Add($"test/{i}/index/123");
            }

            // Setup Web API routes
            for (int i = 0; i < 2500; i++)
            {
                var routeName = $"api_route_{i}";
                var route = new HttpRoute(
                    $"api/test/{i}/{{id}}",
                    new HttpRouteValueDictionary(new { controller = "Api" }),
                    new HttpRouteValueDictionary(new { id = @"\d+" })
                );
                _webApiRoutes.Add(routeName, route);
                _testUrls.Add($"api/test/{i}/123");
            }

            Console.WriteLine($"Setup complete: {_mvcRoutes.Count} MVC routes + {_webApiRoutes.Count} Web API routes");
        }

        public async Task<BenchmarkResults> RunBenchmark(int iterations = 10000)
        {
            Console.WriteLine($"Running benchmark with {iterations} iterations...");
            
            var results = new BenchmarkResults();
            var stopwatch = new Stopwatch();
            var mvcTimes = new List<long>();
            var webApiTimes = new List<long>();

            // Warm up
            Console.WriteLine("Warming up...");
            for (int i = 0; i < 1000; i++)
            {
                var url = _testUrls[i % _testUrls.Count];
                if (url.StartsWith("api/"))
                {
                    var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
                    _webApiRoutes.GetRouteData(request);
                }
                else
                {
                    var httpContext = CreateMockHttpContext(url);
                    _mvcRoutes.GetRouteData(httpContext);
                }
            }

            Console.WriteLine("Running MVC route matching benchmark...");
            for (int i = 0; i < iterations; i++)
            {
                var url = _testUrls[i % _testUrls.Count];
                if (!url.StartsWith("api/"))
                {
                    var httpContext = CreateMockHttpContext(url);
                    stopwatch.Restart();
                    var routeData = _mvcRoutes.GetRouteData(httpContext);
                    stopwatch.Stop();
                    mvcTimes.Add(stopwatch.ElapsedTicks);
                }
            }

            Console.WriteLine("Running Web API route matching benchmark...");
            for (int i = 0; i < iterations; i++)
            {
                var url = _testUrls[i % _testUrls.Count];
                if (url.StartsWith("api/"))
                {
                    var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
                    stopwatch.Restart();
                    var routeData = _webApiRoutes.GetRouteData(request);
                    stopwatch.Stop();
                    webApiTimes.Add(stopwatch.ElapsedTicks);
                }
            }

            results.MvcAverageTimeMs = mvcTimes.Average() * 1000.0 / Stopwatch.Frequency;
            results.WebApiAverageTimeMs = webApiTimes.Average() * 1000.0 / Stopwatch.Frequency;
            results.MvcMinTimeMs = mvcTimes.Min() * 1000.0 / Stopwatch.Frequency;
            results.WebApiMinTimeMs = webApiTimes.Min() * 1000.0 / Stopwatch.Frequency;
            results.MvcMaxTimeMs = mvcTimes.Max() * 1000.0 / Stopwatch.Frequency;
            results.WebApiMaxTimeMs = webApiTimes.Max() * 1000.0 / Stopwatch.Frequency;
            results.MvcP95TimeMs = CalculatePercentile(mvcTimes, 95) * 1000.0 / Stopwatch.Frequency;
            results.WebApiP95TimeMs = CalculatePercentile(webApiTimes, 95) * 1000.0 / Stopwatch.Frequency;
            results.TotalRoutes = _mvcRoutes.Count + _webApiRoutes.Count;

            return results;
        }

        private System.Web.HttpContextBase CreateMockHttpContext(string url)
        {
            var mockContext = new Moq.Mock<System.Web.HttpContextBase>();
            var mockRequest = new Moq.Mock<System.Web.HttpRequestBase>();
            var mockResponse = new Moq.Mock<System.Web.HttpResponseBase>();

            mockRequest.Setup(r => r.AppRelativeCurrentExecutionFilePath).Returns("~" + url);
            mockRequest.Setup(r => r.PathInfo).Returns("");
            mockRequest.Setup(r => r.HttpMethod).Returns("GET");
            mockContext.Setup(c => c.Request).Returns(mockRequest.Object);
            mockContext.Setup(c => c.Response).Returns(mockResponse.Object);

            return mockContext.Object;
        }

        private double CalculatePercentile(List<long> values, int percentile)
        {
            var sorted = values.OrderBy(x => x).ToList();
            var index = (int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1;
            return sorted[index];
        }

        public async Task<OptimizationResults> OptimizeRoutes()
        {
            Console.WriteLine("Analyzing route optimization opportunities...");
            
            var optimizationResults = new OptimizationResults();
            
            // Measure current performance
            var baselineResults = await RunBenchmark(5000);
            optimizationResults.BaselineAverageMs = (baselineResults.MvcAverageTimeMs + baselineResults.WebApiAverageTimeMs) / 2;

            // Optimization 1: Route ordering by frequency
            Console.WriteLine("Applying route ordering optimization...");
            var orderedRoutes = _mvcRoutes.OrderByDescending(r => _testUrls.Count(url => url.Contains(r.Url))).ToList();
            _mvcRoutes.Clear();
            foreach (var route in orderedRoutes)
            {
                _mvcRoutes.Add(route);
            }

            var optimizedResults = await RunBenchmark(5000);
            optimizationResults.OptimizedAverageMs = (optimizedResults.MvcAverageTimeMs + optimizedResults.WebApiAverageTimeMs) / 2;
            optimizationResults.ImprovementPercent = ((baselineResults.MvcAverageTimeMs - optimizedResults.MvcAverageTimeMs) / baselineResults.MvcAverageTimeMs) * 100;

            return optimizationResults;
        }
    }

    public class BenchmarkResults
    {
        public double MvcAverageTimeMs { get; set; }
        public double WebApiAverageTimeMs { get; set; }
        public double MvcMinTimeMs { get; set; }
        public double WebApiMinTimeMs { get; set; }
        public double MvcMaxTimeMs { get; set; }
        public double WebApiMaxTimeMs { get; set; }
        public double MvcP95TimeMs { get; set; }
        public double WebApiP95TimeMs { get; set; }
        public int TotalRoutes { get; set; }
    }

    public class OptimizationResults
    {
        public double BaselineAverageMs { get; set; }
        public double OptimizedAverageMs { get; set; }
        public double ImprovementPercent { get; set; }
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== ASP.NET Routing Performance Benchmark ===");
            Console.WriteLine("Target: 5000 routes with < 1ms response time");
            Console.WriteLine();

            var benchmark = new RoutingBenchmark();
            benchmark.SetupRoutes();

            // Run initial benchmark
            var results = await benchmark.RunBenchmark(10000);
            
            Console.WriteLine("\n=== Benchmark Results ===");
            Console.WriteLine($"Total Routes: {results.TotalRoutes}");
            Console.WriteLine($"MVC Average Time: {results.MvcAverageTimeMs:F4} ms");
            Console.WriteLine($"Web API Average Time: {results.WebApiAverageTimeMs:F4} ms");
            Console.WriteLine($"MVC Min Time: {results.MvcMinTimeMs:F4} ms");
            Console.WriteLine($"Web API Min Time: {results.WebApiMinTimeMs:F4} ms");
            Console.WriteLine($"MVC Max Time: {results.MvcMaxTimeMs:F4} ms");
            Console.WriteLine($"Web API Max Time: {results.WebApiMaxTimeMs:F4} ms");
            Console.WriteLine($"MVC 95th Percentile: {results.MvcP95TimeMs:F4} ms");
            Console.WriteLine($"Web API 95th Percentile: {results.WebApiP95TimeMs:F4} ms");

            // Check if we meet the 1ms target
            var averageTime = (results.MvcAverageTimeMs + results.WebApiAverageTimeMs) / 2;
            Console.WriteLine($"\nOverall Average: {averageTime:F4} ms");
            
            if (averageTime < 1.0)
            {
                Console.WriteLine("✅ TARGET ACHIEVED: Average response time is under 1ms!");
            }
            else
            {
                Console.WriteLine("❌ TARGET NOT MET: Average response time is over 1ms");
                Console.WriteLine("Applying optimizations...");
                
                var optimizationResults = await benchmark.OptimizeRoutes();
                Console.WriteLine($"Optimization improvement: {optimizationResults.ImprovementPercent:F2}%");
            }

            Console.WriteLine("\n=== Performance Recommendations ===");
            Console.WriteLine("1. Use route constraints to reduce matching complexity");
            Console.WriteLine("2. Order routes by frequency of use");
            Console.WriteLine("3. Consider using attribute routing for better performance");
            Console.WriteLine("4. Implement route caching for frequently accessed routes");
            Console.WriteLine("5. Use compiled route expressions where possible");
        }
    }
}