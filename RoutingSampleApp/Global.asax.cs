using System;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using System.Web.Http;
using System.Web.Http.Routing;
using System.Collections.Generic;
using System.Linq;
using System.Web.Optimization;

namespace RoutingSampleApp
{
    public class MvcApplication : HttpApplication
    {
        protected void Application_Start()
        {
            // Configure MVC routes
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            
            // Configure Web API routes
            GlobalConfiguration.Configure(WebApiConfig.Register);
            
            // Configure filters
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            
            // Configure bundles
            BundleConfig.RegisterBundles(BundleTable.Bundles);
        }
    }

    public class RouteConfig
    {
        public static void RegisterRoutes(RouteCollection routes)
        {
            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");

            // Standard routes
            routes.MapRoute(
                name: "Default",
                url: "{controller}/{action}/{id}",
                defaults: new { controller = "Home", action = "Index", id = UrlParameter.Optional }
            );

            // Area routes
            routes.MapRoute(
                name: "Admin",
                url: "admin/{controller}/{action}/{id}",
                defaults: new { area = "Admin", controller = "Dashboard", action = "Index", id = UrlParameter.Optional }
            );

            routes.MapRoute(
                name: "API",
                url: "api/{controller}/{action}/{id}",
                defaults: new { area = "API", controller = "Home", action = "Index", id = UrlParameter.Optional }
            );

            // Generate 5000+ routes for testing
            GenerateMassiveRoutes(routes);
        }

        private static void GenerateMassiveRoutes(RouteCollection routes)
        {
            var controllers = new[] { "Home", "Account", "User", "Product", "Order", "Cart", "Admin", "Api", "Blog", "News",
                "Category", "Search", "Profile", "Settings", "Payment", "Shipping", "Review", "Comment",
                "File", "Image", "Video", "Document", "Report", "Analytics", "Dashboard", "Notification",
                "Message", "Chat", "Forum", "Event", "Calendar", "Task", "Project", "Team", "Company",
                "Customer", "Supplier", "Inventory", "Warehouse", "Logistics", "Finance", "HR", "Legal" };

            var actions = new[] { "Index", "Details", "Create", "Edit", "Delete", "List", "Search", "Filter", "Sort",
                "Export", "Import", "Download", "Upload", "Preview", "Print", "Share", "Like", "Follow",
                "Subscribe", "Unsubscribe", "Verify", "Confirm", "Cancel", "Refund", "Return", "Track",
                "Update", "Save", "Publish", "Draft", "Archive", "Restore", "Move", "Copy", "Clone",
                "Merge", "Split", "Convert", "Transform", "Validate", "Authenticate", "Authorize" };

            var areas = new[] { "admin", "api", "mobile", "desktop", "public", "private", "internal", "external",
                "v1", "v2", "v3", "beta", "alpha", "staging", "dev", "test", "prod", "live",
                "secure", "portal", "dashboard", "console", "panel", "interface", "service" };

            var routeCount = 0;
            var maxRoutes = 5000;

            // Generate simple controller/action routes
            for (int i = 0; i < controllers.Length && routeCount < maxRoutes; i++)
            {
                for (int j = 0; j < actions.Length && routeCount < maxRoutes; j++)
                {
                    routes.MapRoute(
                        name: $"route_{routeCount++}",
                        url: $"{controllers[i].ToLower()}/{actions[j].ToLower()}",
                        defaults: new { controller = controllers[i], action = actions[j] }
                    );
                }
            }

            // Generate area-based routes
            for (int i = 0; i < areas.Length && routeCount < maxRoutes; i++)
            {
                for (int j = 0; j < controllers.Length && routeCount < maxRoutes; j++)
                {
                    for (int k = 0; k < actions.Length && routeCount < maxRoutes; k++)
                    {
                        routes.MapRoute(
                            name: $"route_{routeCount++}",
                            url: $"{areas[i]}/{controllers[j].ToLower()}/{actions[k].ToLower()}",
                            defaults: new { area = areas[i], controller = controllers[j], action = actions[k] }
                        );
                    }
                }
            }

            // Generate complex parameter routes
            for (int i = 0; i < 1000 && routeCount < maxRoutes; i++)
            {
                routes.MapRoute(
                    name: $"route_{routeCount++}",
                    url: $"test/{i}/{{action}}/{{id}}/{{slug}}",
                    defaults: new { controller = "Test", action = "Index", id = UrlParameter.Optional, slug = UrlParameter.Optional },
                    constraints: new { id = @"\d+" }
                );
            }

            // Generate API routes
            for (int i = 0; i < 500 && routeCount < maxRoutes; i++)
            {
                routes.MapRoute(
                    name: $"route_{routeCount++}",
                    url: $"api/v1/{controllers[i % controllers.Length].ToLower()}/{{id}}",
                    defaults: new { controller = controllers[i % controllers.Length], action = "Get", id = UrlParameter.Optional }
                );
            }

            // Generate named routes for specific scenarios
            routes.MapRoute(
                name: "Products",
                url: "products/{category}/{id}",
                defaults: new { controller = "Product", action = "Details" }
            );

            routes.MapRoute(
                name: "Users",
                url: "users/{username}/profile",
                defaults: new { controller = "User", action = "Profile" }
            );

            routes.MapRoute(
                name: "Orders",
                url: "orders/{orderId}/items/{itemId}",
                defaults: new { controller = "Order", action = "Items" }
            );

            routes.MapRoute(
                name: "Blog",
                url: "blog/{year}/{month}/{slug}",
                defaults: new { controller = "Blog", action = "Post" },
                constraints: new { year = @"\d{4}", month = @"\d{2}" }
            );

            routes.MapRoute(
                name: "Search",
                url: "search/{query}/{page}",
                defaults: new { controller = "Search", action = "Results", page = 1 }
            );

            routes.MapRoute(
                name: "Files",
                url: "files/{path}/{filename}",
                defaults: new { controller = "File", action = "Download" }
            );

            routes.MapRoute(
                name: "Reports",
                url: "reports/{type}/{date}",
                defaults: new { controller = "Report", action = "Generate" }
            );

            routes.MapRoute(
                name: "Analytics",
                url: "analytics/{metric}/{period}",
                defaults: new { controller = "Analytics", action = "View" }
            );

            routes.MapRoute(
                name: "Notifications",
                url: "notifications/{type}/{id}",
                defaults: new { controller = "Notification", action = "View" }
            );

            routes.MapRoute(
                name: "Messages",
                url: "messages/{threadId}/{messageId}",
                defaults: new { controller = "Message", action = "View" }
            );

            routes.MapRoute(
                name: "Events",
                url: "events/{eventId}/attendees/{attendeeId}",
                defaults: new { controller = "Event", action = "Attendee" }
            );

            routes.MapRoute(
                name: "Projects",
                url: "projects/{projectId}/tasks/{taskId}",
                defaults: new { controller = "Project", action = "Task" }
            );

            routes.MapRoute(
                name: "Teams",
                url: "teams/{teamId}/members/{memberId}",
                defaults: new { controller = "Team", action = "Member" }
            );

            routes.MapRoute(
                name: "Companies",
                url: "companies/{companyId}/departments/{deptId}",
                defaults: new { controller = "Company", action = "Department" }
            );

            routes.MapRoute(
                name: "Customers",
                url: "customers/{customerId}/orders/{orderId}",
                defaults: new { controller = "Customer", action = "Order" }
            );

            routes.MapRoute(
                name: "Inventory",
                url: "inventory/{warehouseId}/items/{itemId}",
                defaults: new { controller = "Inventory", action = "Item" }
            );

            routes.MapRoute(
                name: "Finance",
                url: "finance/{accountId}/transactions/{txnId}",
                defaults: new { controller = "Finance", action = "Transaction" }
            );

            routes.MapRoute(
                name: "HR",
                url: "hr/{employeeId}/documents/{docId}",
                defaults: new { controller = "HR", action = "Document" }
            );

            Console.WriteLine($"Generated {routeCount} MVC routes");
        }
    }

    public class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            // Web API configuration and services
            config.MapHttpAttributeRoutes();

            // Web API routes
            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );

            // Generate API routes
            GenerateApiRoutes(config);
        }

        private static void GenerateApiRoutes(HttpConfiguration config)
        {
            var controllers = new[] { "Home", "User", "Product", "Order", "Cart", "Admin", "Api", "Blog", "News",
                "Category", "Search", "Profile", "Settings", "Payment", "Shipping", "Review", "Comment",
                "File", "Image", "Video", "Document", "Report", "Analytics", "Dashboard", "Notification",
                "Message", "Chat", "Forum", "Event", "Calendar", "Task", "Project", "Team", "Company",
                "Customer", "Supplier", "Inventory", "Warehouse", "Logistics", "Finance", "HR", "Legal" };

            var routeCount = 0;
            var maxRoutes = 1000;

            // Generate API routes
            for (int i = 0; i < controllers.Length && routeCount < maxRoutes; i++)
            {
                config.Routes.MapHttpRoute(
                    name: $"api_route_{routeCount++}",
                    routeTemplate: $"api/{controllers[i].ToLower()}/{{id}}",
                    defaults: new { controller = controllers[i], id = RouteParameter.Optional }
                );
            }

            // Generate versioned API routes
            for (int i = 0; i < controllers.Length && routeCount < maxRoutes; i++)
            {
                config.Routes.MapHttpRoute(
                    name: $"api_route_{routeCount++}",
                    routeTemplate: $"api/v1/{controllers[i].ToLower()}/{{id}}",
                    defaults: new { controller = controllers[i], id = RouteParameter.Optional }
                );
            }

            // Generate complex API routes
            for (int i = 0; i < 500 && routeCount < maxRoutes; i++)
            {
                config.Routes.MapHttpRoute(
                    name: $"api_route_{routeCount++}",
                    routeTemplate: $"api/{controllers[i % controllers.Length].ToLower()}/{{id}}/{{action}}",
                    defaults: new { controller = controllers[i % controllers.Length], action = "Get" }
                );
            }

            Console.WriteLine($"Generated {routeCount} Web API routes");
        }
    }

    public class FilterConfig
    {
        public static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
            filters.Add(new HandleErrorAttribute());
        }
    }

    public class BundleConfig
    {
        public static void RegisterBundles(BundleCollection bundles)
        {
            bundles.Add(new ScriptBundle("~/bundles/jquery").Include(
                        "~/Scripts/jquery-{version}.js"));

            bundles.Add(new ScriptBundle("~/bundles/jqueryval").Include(
                        "~/Scripts/jquery.validate*"));

            bundles.Add(new ScriptBundle("~/bundles/modernizr").Include(
                        "~/Scripts/modernizr-*"));

            bundles.Add(new ScriptBundle("~/bundles/bootstrap").Include(
                      "~/Scripts/bootstrap.js"));

            bundles.Add(new StyleBundle("~/Content/css").Include(
                      "~/Content/bootstrap.css",
                      "~/Content/site.css"));
        }
    }
}