using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Routing;

namespace RoutingSampleApp.Controllers
{
    public class ApiController : System.Web.Http.ApiController
    {
        // GET api/api
        public IHttpActionResult Get()
        {
            return Ok(new { message = "API Controller - Get All", timestamp = DateTime.Now });
        }

        // GET api/api/5
        public IHttpActionResult Get(int id)
        {
            return Ok(new { message = $"API Controller - Get {id}", id = id, timestamp = DateTime.Now });
        }

        // POST api/api
        public IHttpActionResult Post([FromBody] string value)
        {
            return Ok(new { message = "API Controller - Post", value = value, timestamp = DateTime.Now });
        }

        // PUT api/api/5
        public IHttpActionResult Put(int id, [FromBody] string value)
        {
            return Ok(new { message = $"API Controller - Put {id}", id = id, value = value, timestamp = DateTime.Now });
        }

        // DELETE api/api/5
        public IHttpActionResult Delete(int id)
        {
            return Ok(new { message = $"API Controller - Delete {id}", id = id, timestamp = DateTime.Now });
        }

        // GET api/api/search?query=test
        [HttpGet]
        public IHttpActionResult Search(string query)
        {
            return Ok(new { message = "API Controller - Search", query = query, timestamp = DateTime.Now });
        }

        // GET api/api/filter?category=electronics&price=100
        [HttpGet]
        public IHttpActionResult Filter(string category, decimal price)
        {
            return Ok(new { message = "API Controller - Filter", category = category, price = price, timestamp = DateTime.Now });
        }

        // GET api/api/routeinfo
        [HttpGet]
        public IHttpActionResult RouteInfo()
        {
            var routeInfo = new
            {
                message = "API Controller - Route Info",
                currentRoute = Request.GetRouteData()?.Values,
                routeCount = GlobalConfiguration.Configuration.Routes.Count,
                timestamp = DateTime.Now
            };
            return Ok(routeInfo);
        }
    }

    public class UserController : System.Web.Http.ApiController
    {
        public IHttpActionResult Get()
        {
            return Ok(new { message = "User API - Get All Users", timestamp = DateTime.Now });
        }

        public IHttpActionResult Get(int id)
        {
            return Ok(new { message = $"User API - Get User {id}", id = id, timestamp = DateTime.Now });
        }

        public IHttpActionResult Post([FromBody] UserModel user)
        {
            return Ok(new { message = "User API - Create User", user = user, timestamp = DateTime.Now });
        }

        public IHttpActionResult Put(int id, [FromBody] UserModel user)
        {
            return Ok(new { message = $"User API - Update User {id}", id = id, user = user, timestamp = DateTime.Now });
        }

        public IHttpActionResult Delete(int id)
        {
            return Ok(new { message = $"User API - Delete User {id}", id = id, timestamp = DateTime.Now });
        }
    }

    public class ProductController : System.Web.Http.ApiController
    {
        public IHttpActionResult Get()
        {
            return Ok(new { message = "Product API - Get All Products", timestamp = DateTime.Now });
        }

        public IHttpActionResult Get(int id)
        {
            return Ok(new { message = $"Product API - Get Product {id}", id = id, timestamp = DateTime.Now });
        }

        public IHttpActionResult Post([FromBody] ProductModel product)
        {
            return Ok(new { message = "Product API - Create Product", product = product, timestamp = DateTime.Now });
        }

        public IHttpActionResult Put(int id, [FromBody] ProductModel product)
        {
            return Ok(new { message = $"Product API - Update Product {id}", id = id, product = product, timestamp = DateTime.Now });
        }

        public IHttpActionResult Delete(int id)
        {
            return Ok(new { message = $"Product API - Delete Product {id}", id = id, timestamp = DateTime.Now });
        }
    }

    public class OrderController : System.Web.Http.ApiController
    {
        public IHttpActionResult Get()
        {
            return Ok(new { message = "Order API - Get All Orders", timestamp = DateTime.Now });
        }

        public IHttpActionResult Get(int id)
        {
            return Ok(new { message = $"Order API - Get Order {id}", id = id, timestamp = DateTime.Now });
        }

        public IHttpActionResult Post([FromBody] OrderModel order)
        {
            return Ok(new { message = "Order API - Create Order", order = order, timestamp = DateTime.Now });
        }

        public IHttpActionResult Put(int id, [FromBody] OrderModel order)
        {
            return Ok(new { message = $"Order API - Update Order {id}", id = id, order = order, timestamp = DateTime.Now });
        }

        public IHttpActionResult Delete(int id)
        {
            return Ok(new { message = $"Order API - Delete Order {id}", id = id, timestamp = DateTime.Now });
        }
    }

    // Model classes
    public class UserModel
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
    }

    public class ProductModel
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
        public string Category { get; set; }
    }

    public class OrderModel
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public List<OrderItemModel> Items { get; set; }
        public decimal Total { get; set; }
    }

    public class OrderItemModel
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
    }
}