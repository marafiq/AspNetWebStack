using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;

namespace RoutingSampleApp.Controllers
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            ViewBag.Message = "Welcome to ASP.NET MVC with 5000+ routes!";
            ViewBag.RouteCount = RouteTable.Routes.Count;
            ViewBag.Routes = GetSampleRoutes();
            return View();
        }

        public ActionResult About()
        {
            ViewBag.Message = "Your application description page.";
            return View();
        }

        public ActionResult Contact()
        {
            ViewBag.Message = "Your contact page.";
            return View();
        }

        public ActionResult Details(int id)
        {
            ViewBag.Id = id;
            ViewBag.Message = $"Details for item {id}";
            return View();
        }

        public ActionResult Search(string query, int page = 1)
        {
            ViewBag.Query = query;
            ViewBag.Page = page;
            ViewBag.Message = $"Search results for '{query}' (page {page})";
            return View();
        }

        public ActionResult Products(string category, int id)
        {
            ViewBag.Category = category;
            ViewBag.Id = id;
            ViewBag.Message = $"Product {id} in category {category}";
            return View();
        }

        public ActionResult Users(string username)
        {
            ViewBag.Username = username;
            ViewBag.Message = $"Profile for user {username}";
            return View();
        }

        public ActionResult Orders(int orderId, int itemId)
        {
            ViewBag.OrderId = orderId;
            ViewBag.ItemId = itemId;
            ViewBag.Message = $"Order {orderId}, Item {itemId}";
            return View();
        }

        public ActionResult Blog(int year, int month, string slug)
        {
            ViewBag.Year = year;
            ViewBag.Month = month;
            ViewBag.Slug = slug;
            ViewBag.Message = $"Blog post: {slug} ({month}/{year})";
            return View();
        }

        public ActionResult Files(string path, string filename)
        {
            ViewBag.Path = path;
            ViewBag.Filename = filename;
            ViewBag.Message = $"File: {path}/{filename}";
            return View();
        }

        public ActionResult Reports(string type, string date)
        {
            ViewBag.Type = type;
            ViewBag.Date = date;
            ViewBag.Message = $"Report: {type} for {date}";
            return View();
        }

        public ActionResult Analytics(string metric, string period)
        {
            ViewBag.Metric = metric;
            ViewBag.Period = period;
            ViewBag.Message = $"Analytics: {metric} ({period})";
            return View();
        }

        public ActionResult Notifications(string type, int id)
        {
            ViewBag.Type = type;
            ViewBag.Id = id;
            ViewBag.Message = $"Notification {id} of type {type}";
            return View();
        }

        public ActionResult Messages(int threadId, int messageId)
        {
            ViewBag.ThreadId = threadId;
            ViewBag.MessageId = messageId;
            ViewBag.Message = $"Message {messageId} in thread {threadId}";
            return View();
        }

        public ActionResult Events(int eventId, int attendeeId)
        {
            ViewBag.EventId = eventId;
            ViewBag.AttendeeId = attendeeId;
            ViewBag.Message = $"Event {eventId}, Attendee {attendeeId}";
            return View();
        }

        public ActionResult Projects(int projectId, int taskId)
        {
            ViewBag.ProjectId = projectId;
            ViewBag.TaskId = taskId;
            ViewBag.Message = $"Project {projectId}, Task {taskId}";
            return View();
        }

        public ActionResult Teams(int teamId, int memberId)
        {
            ViewBag.TeamId = teamId;
            ViewBag.MemberId = memberId;
            ViewBag.Message = $"Team {teamId}, Member {memberId}";
            return View();
        }

        public ActionResult Companies(int companyId, int deptId)
        {
            ViewBag.CompanyId = companyId;
            ViewBag.DeptId = deptId;
            ViewBag.Message = $"Company {companyId}, Department {deptId}";
            return View();
        }

        public ActionResult Customers(int customerId, int orderId)
        {
            ViewBag.CustomerId = customerId;
            ViewBag.OrderId = orderId;
            ViewBag.Message = $"Customer {customerId}, Order {orderId}";
            return View();
        }

        public ActionResult Inventory(int warehouseId, int itemId)
        {
            ViewBag.WarehouseId = warehouseId;
            ViewBag.ItemId = itemId;
            ViewBag.Message = $"Warehouse {warehouseId}, Item {itemId}";
            return View();
        }

        public ActionResult Finance(int accountId, int txnId)
        {
            ViewBag.AccountId = accountId;
            ViewBag.TxnId = txnId;
            ViewBag.Message = $"Account {accountId}, Transaction {txnId}";
            return View();
        }

        public ActionResult HR(int employeeId, int docId)
        {
            ViewBag.EmployeeId = employeeId;
            ViewBag.DocId = docId;
            ViewBag.Message = $"Employee {employeeId}, Document {docId}";
            return View();
        }

        // Child Actions for testing @Html.RenderAction()
        [ChildActionOnly]
        public ActionResult Navigation()
        {
            var navItems = new List<NavigationItem>
            {
                new NavigationItem { Text = "Home", Action = "Index", Controller = "Home" },
                new NavigationItem { Text = "Products", Action = "Products", Controller = "Home" },
                new NavigationItem { Text = "Users", Action = "Users", Controller = "Home" },
                new NavigationItem { Text = "Orders", Action = "Orders", Controller = "Home" },
                new NavigationItem { Text = "Blog", Action = "Blog", Controller = "Home" },
                new NavigationItem { Text = "Search", Action = "Search", Controller = "Home" }
            };
            return PartialView("_Navigation", navItems);
        }

        [ChildActionOnly]
        public ActionResult Sidebar()
        {
            var sidebarItems = new List<SidebarItem>
            {
                new SidebarItem { Title = "Quick Links", Links = new List<string> { "Dashboard", "Reports", "Analytics" } },
                new SidebarItem { Title = "Recent Activity", Links = new List<string> { "New Order", "User Login", "File Upload" } },
                new SidebarItem { Title = "System Status", Links = new List<string> { "Online", "Healthy", "Updated" } }
            };
            return PartialView("_Sidebar", sidebarItems);
        }

        [ChildActionOnly]
        public ActionResult Footer()
        {
            ViewBag.Year = DateTime.Now.Year;
            ViewBag.RouteCount = RouteTable.Routes.Count;
            return PartialView("_Footer");
        }

        // URL Generation Test Actions
        public ActionResult UrlGenerationTest()
        {
            ViewBag.Urls = GenerateTestUrls();
            return View();
        }

        public ActionResult RouteInfo()
        {
            var routeInfo = new RouteInfo
            {
                TotalRoutes = RouteTable.Routes.Count,
                CurrentRoute = RouteData.Values,
                RouteConstraints = GetRouteConstraints(),
                SampleRoutes = GetSampleRoutes()
            };
            return View(routeInfo);
        }

        private List<string> GenerateTestUrls()
        {
            var urls = new List<string>();
            var urlHelper = new UrlHelper(Request.RequestContext);

            // Test various URL generation scenarios
            urls.Add(urlHelper.Action("Index", "Home"));
            urls.Add(urlHelper.Action("Details", "Home", new { id = 123 }));
            urls.Add(urlHelper.Action("Search", "Home", new { query = "test", page = 1 }));
            urls.Add(urlHelper.RouteUrl("Products", new { category = "electronics", id = 456 }));
            urls.Add(urlHelper.RouteUrl("Users", new { username = "john_doe" }));
            urls.Add(urlHelper.RouteUrl("Orders", new { orderId = 789, itemId = 101 }));
            urls.Add(urlHelper.RouteUrl("Blog", new { year = 2024, month = 01, slug = "test-post" }));
            urls.Add(urlHelper.RouteUrl("Search", new { query = "search-term", page = 1 }));
            urls.Add(urlHelper.RouteUrl("Files", new { path = "documents", filename = "report.pdf" }));
            urls.Add(urlHelper.RouteUrl("Reports", new { type = "sales", date = "2024-01-15" }));

            return urls;
        }

        private List<SampleRoute> GetSampleRoutes()
        {
            var routes = new List<SampleRoute>();
            var routeData = RouteTable.Routes;

            // Get sample routes from the route collection
            foreach (Route route in routeData.OfType<Route>().Take(20))
            {
                routes.Add(new SampleRoute
                {
                    Name = route.DataTokens?["Name"]?.ToString() ?? "Unnamed",
                    Url = route.Url,
                    Defaults = route.Defaults?.Keys.ToList() ?? new List<string>(),
                    Constraints = route.Constraints?.Keys.ToList() ?? new List<string>()
                });
            }

            return routes;
        }

        private Dictionary<string, string> GetRouteConstraints()
        {
            var constraints = new Dictionary<string, string>();
            var routeData = RouteTable.Routes;

            foreach (Route route in routeData.OfType<Route>())
            {
                if (route.Constraints != null)
                {
                    foreach (var constraint in route.Constraints)
                    {
                        constraints[constraint.Key] = constraint.Value.ToString();
                    }
                }
            }

            return constraints;
        }
    }

    public class NavigationItem
    {
        public string Text { get; set; }
        public string Action { get; set; }
        public string Controller { get; set; }
    }

    public class SidebarItem
    {
        public string Title { get; set; }
        public List<string> Links { get; set; }
    }

    public class RouteInfo
    {
        public int TotalRoutes { get; set; }
        public RouteValueDictionary CurrentRoute { get; set; }
        public Dictionary<string, string> RouteConstraints { get; set; }
        public List<SampleRoute> SampleRoutes { get; set; }
    }

    public class SampleRoute
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public List<string> Defaults { get; set; }
        public List<string> Constraints { get; set; }
    }
}