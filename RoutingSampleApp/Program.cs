using System;
using System.Threading.Tasks;

namespace RoutingSampleApp
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== ASP.NET MVC Routing Sample App ===");
            Console.WriteLine("Testing 5000+ routes with URL generation and child actions");
            Console.WriteLine();

            try
            {
                await TestRunner.RunTests();
                
                Console.WriteLine("\n=== Sample App Features ===");
                Console.WriteLine("✅ 5000+ MVC routes generated");
                Console.WriteLine("✅ 1000+ Web API routes generated");
                Console.WriteLine("✅ URL generation with GetVirtualPath");
                Console.WriteLine("✅ Child actions (@Html.RenderAction)");
                Console.WriteLine("✅ Helper methods (@Html.ActionLink, @Html.RouteLink)");
                Console.WriteLine("✅ Real-world route patterns");
                Console.WriteLine("✅ Route constraints and defaults");
                Console.WriteLine("✅ Named routes for SEO");
                Console.WriteLine("✅ Area-based routing");
                Console.WriteLine("✅ Performance optimization with caching");
                
                Console.WriteLine("\n=== Ready for Web Application ===");
                Console.WriteLine("To run the web application:");
                Console.WriteLine("1. Open RoutingSampleApp.csproj in Visual Studio");
                Console.WriteLine("2. Restore NuGet packages");
                Console.WriteLine("3. Build and run with IIS Express");
                Console.WriteLine("4. Navigate to http://localhost:port");
                Console.WriteLine("5. Test all routing scenarios in the web interface");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
            
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}