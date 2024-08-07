﻿using Asp.Versioning;
using BookShop.API.Controllers.Services;
using BookShop.API.Models;
using BookShop.API.Models.Authentication;
using DnsClient.Protocol;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Net;

namespace BookShop.API.Controllers
{
    [ApiController]
    [ApiVersion("1")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [Authorize(Roles = ApiConstants.Admin)]
    [EnableCors(PolicyName = ApiConstants.CorsNameAdmin)]
    [Route("account/")]
    public class OrderV1Controller(ILogger<OrderV1Controller> logger, UserManager<ApiUser> userManager, StockDBServices stockServices, AuthenticationApiDbContext dbContext) : ControllerBase
    {
        private readonly ILogger<OrderV1Controller> _logger = logger;
        private readonly UserManager<ApiUser> _userManager = userManager;
        private readonly StockDBServices _stockServices = stockServices;
        private readonly AuthenticationApiDbContext _dbContext = dbContext;

        #region of Help Methods
        private void LogingError(Exception error) => _logger.LogError(message: error.Message, args: error.StackTrace);
        private void LogingInformation(string message) => _logger.LogInformation(message: message);
        private void LogingWarning(string message) => _logger.LogWarning(message: message);
        private ActionResult Warning(string message, int statusCode)
        {
            OrderDisplayModel model = new(new Order(), message);

            LogingWarning(message);
            return statusCode == (int)HttpStatusCode.Unauthorized ?
                Unauthorized(model) :
                statusCode == (int)HttpStatusCode.NotFound ?
                NotFound(model) :
                statusCode == (int)HttpStatusCode.BadRequest ?
                BadRequest(model) :
                UnprocessableEntity(model);
        }
        private ActionResult Successfull(OrderDisplayModel model)
        {
            LogingInformation(model.ToJson());
            return Ok(model);
        }
        private ActionResult Successfull(List<OrderDisplayModel> orders)
        {
            LogingInformation(orders.ToJson());
            return Ok(orders);
        }
        private ActionResult Successfull(bool data)
        {
            return Ok(data);
        }
        private ActionResult Error(Exception ex)
        {
            LogingError(ex);
            return Problem(ex.Message);
        }

        private string MessageUnavailableProducts(List<string> productsIdsNotFound, bool orderIsSubmitted)
        {
            string message = "";
            if (productsIdsNotFound.Count != 0)
            {
                message += "Some product from your order, currently unavailable in stock.";
                message += orderIsSubmitted ?
                    "There no detailed data can be displayed for products with IDs: " :
                    "Total Order price recounted and products was removed from your Order." +
                    "Removed Products IDs: ";
                int index = productsIdsNotFound.Count;
                foreach (string id in productsIdsNotFound)
                {
                    --index;
                    message += index != 0 ? id + ", " : id + ".";
                }
            }
            return message;
        }
        #endregion

        //Creates new order, if there is not unsubmitted orders, for the current authorized user,
        //else returns Problem() object result with information message that contains
        //id for unsubmitted order.
        //User, should add new products to last not submitted order and submit it,
        //before to start another new order.
        [HttpPost, Route("/order")]
        public async Task<ActionResult<OrderDisplayModel>> PostOrder([FromQuery]List<string> productsIds)
        {
            try
            {
                string message = "";
                //checks if user is signed in to create order
                var user = await _userManager.FindByNameAsync(User.Identity!.Name!);
                user ??= await _userManager.FindByEmailAsync(User.Identity!.Name!);
                if (user is not null)
                {
                    var listOfOrders = await _dbContext.Orders.Where(_ => _.UserId.Equals(user!.Id)).ToListAsync();
                    //checks if user has an uncompleted orders before to create new order
                    if (listOfOrders.Any(_ => !_.SubmittedOrder))
                    {
                        Order? order = listOfOrders.FirstOrDefault(_ => !_.SubmittedOrder);
                        return Warning("OrderId: " + listOfOrders.FirstOrDefault(_ => !_.SubmittedOrder!)!.OrderId +
                            ". Please submit order above before to create new order", 0);
                    }

                    Order orderToPost = new()
                    {
                        OrderId = Guid.NewGuid().ToString(),
                        User = user,
                        UserId = user.Id,
                        TotalPrice = 0,
                        OrderDateTime = DateTime.Now,
                        SubmittedOrder = false
                    };
                    //checks if provided products ids exists and if they are available in stock
                    //adds the ids of products to the order
                    foreach (string id in productsIds)
                    {
                        Product product = await _stockServices.GetBookByIdAsync(id);

                        if (product?.IsAvailable == true)
                        {
                            orderToPost.ProductsId!.Add(product.Id!);
                            orderToPost.TotalPrice += product.Price;
                        }
                        else
                        {
                            message += "The product with ID: " + id;
                            message += product is null ?
                                ", was not found in stock, please check if ID is correct." :
                                ", currently is unavailble.";
                            return Warning(message + " Unable to process your order.", (int)HttpStatusCode.NotFound);
                        }
                    }

                    _dbContext.Orders.Add(orderToPost);
                    var result = await _dbContext.SaveChangesAsync();
                    
                    if (result == 0)
                    {
                        return Warning("Unable to process request. Order was not saved.", (int)HttpStatusCode.BadRequest);
                    }
                    else
                    {
                        OrderDisplayModel model = new(orderToPost, "Order created successfully");
                        return Successfull(model);
                    }

                }
                return Warning("User was not found in system, please ensure that you signed in", (int)HttpStatusCode.BadRequest);
            }
            catch (Exception ex)
            {
                return Error(ex);
            }
        }

        //TODO return total number of products added
        //TODO return ID's of added products
        //TODO if any products deleted from order, because not available, return total number of deleted products
        //Adds more products to existing order for the current authorized user
        [HttpPut, Route("/order/products/add")]
        public async Task<ActionResult<OrderDisplayModel>> PutOrderAddProducts([FromQuery]List<string> productsIds, [FromQuery][Required] string orderId)
        {
            try
            {
                var user = await _userManager.FindByNameAsync(User.Identity!.Name!);
                user ??= await _userManager.FindByEmailAsync(User.Identity!.Name!);
                string message = "";
                if (user is not null)
                {
                    Order? order = await _dbContext.Orders.FirstOrDefaultAsync(order => order.UserId.Equals(user.Id) && order.OrderId.Equals(orderId));

                    //Checks if order with requested id exists and is not submitted yet
                    if (order is not null && !order.SubmittedOrder)
                    {
                        //Checks if previously added products in order are still available
                        List<string> productsNotAvailable = [];
                        foreach (string id in order.ProductsId!)
                        {
                            Product product = await _stockServices.GetBookByIdAsync(id);                          
                            if (product is null || !product.IsAvailable)
                            {
                                productsNotAvailable.Add(id);
                            }
                        }
                        if (productsNotAvailable.Count != 0)
                        {
                            message += MessageUnavailableProducts(productsNotAvailable, order.SubmittedOrder);

                            order.ProductsId =
                                [.. order.ProductsId.Where(id => !productsNotAvailable.Contains(id))];
                        }
                        //Checks if currently requested products are in stock
                        foreach (string id in productsIds)
                        {
                            Product product = await _stockServices.GetBookByIdAsync(id);

                            if (product?.IsAvailable == true)
                            {
                                order.ProductsId!.Add(product.Id!);
                            }
                            else
                            {
                                message += "The product with ID: " + id + ", was not added to the order, because product ";
                                message += product is null ?
                                    " was not found in stock, please check if ID is correct." :
                                    " currently is unavailble.";
                            }
                        }
                        //recounts/resets ordr total price
                        decimal price = 0;
                        foreach (string id in order.ProductsId)
                        {
                            price += (await _stockServices.GetBookByIdAsync(id)).Price;
                        }
                        order.TotalPrice = price;
                        order.OrderDateTime = DateTime.Now;
                        _dbContext.Orders.Update(order);
                        var result = await _dbContext.SaveChangesAsync();

                        if (result == 0)
                        {
                            return Warning("Unable to process request. Products was not added", (int)HttpStatusCode.BadRequest);
                        }
                        else
                        {
                            message += " Order updated successfully";
                            return Successfull(new OrderDisplayModel(order, message));
                        }
                    }
                    else
                    {
                        message = "Sorry, the requested order, with id: " + orderId;
                        message += order is not null ?
                         ", already submitted. Please start new order." :
                         ", was not found. Please start new order.";
                        return Warning(message + "Request declined at: " + DateTime.Now, (int)HttpStatusCode.BadRequest);
                    }
                }
                else
                {
                    return Warning("User was not found in system, please ensure that you signed in", (int)HttpStatusCode.BadRequest);
                }
            }
            catch (Exception ex)
            {
                return Error(ex);
            }
        }

        //Deletes products from existing order for the current authorized user
        [HttpPut, Route("/order/products/delete")]
        public async Task<ActionResult> PutOrderDeleteProducts([FromQuery]List<string>productIds, [FromQuery][Required]string orderId)
        {
            try
            {
                var user = await _userManager.FindByNameAsync(User.Identity!.Name!);
                user ??= await _userManager.FindByEmailAsync(User.Identity!.Name!);
                string message = "";
                if (user is not null)
                {
                    Order? order = await _dbContext.Orders.FirstOrDefaultAsync(order => order.UserId.Equals(user.Id) && order.OrderId.Equals(orderId));

                    //Checks if order with requested id exists and is not submitted yet
                    if (order?.SubmittedOrder == false)
                    {
                        foreach (string id in productIds)
                        {
                            if (order.ProductsId!.Contains(id))
                            {
                                order.ProductsId.Remove(id);
                            }
                        }
                        
                        //Checking if the rest of products in order isAvailable and recounting total price of order
                        order.TotalPrice = 0;
                        foreach(string id in order.ProductsId!)
                        {
                            Product product = await _stockServices.GetBookByIdAsync(id);
                            if(product?.IsAvailable == true)
                            {
                                order.TotalPrice += product.Price;
                            }
                            else
                            {
                                order.ProductsId.Remove(id);
                            }
                            
                        }
                        order.OrderDateTime = DateTime.Now;
                        _dbContext.Orders.Update(order);
                        var result = await _dbContext.SaveChangesAsync();

                        string info = "OrderID: " + order.OrderId + ", at DateTime: " + order.OrderDateTime;
                        if (result == 0)
                        {
                            return Warning("Unable to process request. Products was not removed " +
                                info, (int)HttpStatusCode.BadRequest);
                        }
                        else return Successfull(new OrderDisplayModel(order, "Products removed successfully, " + info));
                    }
                    else
                    {
                        message = "Sorry, the requested order, with id: " + orderId;
                        message += order is not null ?
                         ", already submitted. Please start new order." :
                         ", was not found. Please start new order.";
                        return Warning(message + "Request declined at: " + DateTime.Now, (int)HttpStatusCode.BadRequest);
                    }
                }
                else
                {
                    return Warning("User was not found in system, please ensure that you signed in", (int)HttpStatusCode.BadRequest);
                }
            }
            catch (Exception ex)
            {
                return Error(ex);
            }
        }

        //TODO if any products deleted from order, because not available, return total number of deleted products
        //Submitts orders
        //Checks if there all products still available in stock before submmiting.
        //If some products is not available, then deletes it from order, recounting total price and
        //informing the user about this and that the order needs to be double-checked and resubmit it
        [HttpPut, Route("/order/submit")]
        public async Task<ActionResult> PutOrderAsSubmitted([FromQuery]string orderId)
        {
            try
            {
                var user = await _userManager.FindByNameAsync(User.Identity!.Name!);
                user ??= await _userManager.FindByEmailAsync(User.Identity!.Name!);
                string message = "";
                if (user is not null)
                {
                    Order? order = await _dbContext.Orders.FirstOrDefaultAsync(order => order.UserId.Equals(user.Id) && order.OrderId.Equals(orderId));

                    //Checks if order with requested id exists and is not submitted yet
                    if (order is not null && !order.SubmittedOrder)
                    {
                        //Checks if all products in order exists and available
                        if (order.ProductsId!.Count != 0)
                        {
                            string idsDeleted = "";
                            List<string> productsIds = [.. order.ProductsId!];
                            List<string> productsIdNotAvailable = [];
                            foreach (string productId in productsIds)
                            {
                                Product product = await _stockServices.GetBookByIdAsync(productId);
                                if(product is null || !product.IsAvailable)
                                {
                                    idsDeleted += productId + ", ";
                                    productsIdNotAvailable.Add(productId);
                                    order.ProductsId!.Remove(productId);
                                }
                            }
                            //Recounts total price for order if there was removed any not in stock products
                            if (productsIdNotAvailable.Count != 0)
                            {
                                decimal totalPrice = 0;
                                foreach (string producId in productsIds)
                                {
                                    Product product = await _stockServices.GetBookByIdAsync(producId);
                                    totalPrice += product is not null && product.IsAvailable ? product.Price : 0;
                                }
                                order.TotalPrice = totalPrice;
                            }
                            //Resetting order 
                            order.SubmittedOrder = productsIdNotAvailable.Count == 0;
                            order.OrderDateTime = DateTime.Now;
                            _dbContext.Orders.Update(order);

                            var result = await _dbContext.SaveChangesAsync();

                            //Checks the result 
                            if(result == 0)
                            {
                                return Warning("Unable to process request. Order was not saved, OrderID: " + order.OrderId, (int)HttpStatusCode.BadRequest);
                            }
                            else
                            {
                                //if there was deleted any products from Order, needs user to recheck what is
                                //in it before submitting
                                if (!order.SubmittedOrder)
                                {
                                    message = "The product/products with id: " + idsDeleted + ", was not found or        currently unavailable. Those products was removed from your order. Order was not subbmitted. Please recheck the order and resubmit it again.";
                                    return Warning(message, (int)HttpStatusCode.NotFound);
                                }
                                else return Successfull(new OrderDisplayModel(order, "Order submitted successfully, at: " + order.OrderDateTime));
                            }
                        }
                        else
                        {
                            return Warning("Can not submit empty Order, Order should have at list one product. Request declined at: " + DateTime.Now, (int)HttpStatusCode.BadRequest);
                        }
                    }
                    else
                    {
                        message = "Sorry, the requested order, with id: " + orderId;
                        message += order is not null ?
                         ", already submitted." :
                         ", was not found.";
                        return Warning(message + "Request declined at: " + DateTime.Now, (int)HttpStatusCode.BadRequest);
                    }
                }
                else
                {
                    return Warning("User was not found in system, please ensure that you signed in", (int)HttpStatusCode.BadRequest);
                }
            }
            catch (Exception ex)
            {
                return Error(ex);
            }
        }

        //For ApiVersion 1, unsubmitting any submitted order
        [HttpPut, Route("order/unsubmit")]
        [Authorize(Roles = ApiConstants.Admin)]
        public async Task<ActionResult<OrderDisplayModel>> PutOrderAsUnsubmitted([FromQuery]string orderId)
        {
            try
            {
                Order? order = await _dbContext.Orders.FirstOrDefaultAsync(_ => _.OrderId.Equals(orderId));
                if(order is not null)
                {
                    order.SubmittedOrder = false;
                    _dbContext.Orders.Update(order);
                    var result = await _dbContext.SaveChangesAsync();
                    return result != 0 ? 
                        Successfull(new OrderDisplayModel(order, "Order: " + order.OrderId + ", successfully unsubmitted")) :
                        Warning("Unable to process your request for order: " + order.OrderId, 
                        (int)HttpStatusCode.BadRequest);
                }
                return Warning("Order with ID: " + orderId + ", was not found", (int)HttpStatusCode.NotFound);
            }
            catch(Exception ex)
            {
                return Error(ex);
            }
        }

        //For ApiVersion 1, can information about any order, version for users in admin role
        [HttpGet, Route("/order/details")]
        [Authorize(Roles = ApiConstants.Admin)]
        public async Task<ActionResult<OrderDisplayModel>> GetOrder([FromQuery]string orderId)
        {
            try
            {
                string info = "";
                var user = await _userManager.FindByNameAsync(User.Identity!.Name!);
                user ??= await _userManager.FindByEmailAsync(User.Identity!.Name!);
                if (user is not null)
                {
                    Order? order = await _dbContext.Orders.FirstOrDefaultAsync(o => o.OrderId.Equals(orderId));
                    if (order is not null)
                    {
                        List<Product> productsAvailable = [];
                        List<string> productNotAvailable = [];
                        foreach(string productId in order.ProductsId!)
                        {
                            Product product = await _stockServices.GetBookByIdAsync(productId);
                            if(product?.IsAvailable == true )
                            {
                                productsAvailable.Add(product);
                            }
                            else
                            {
                                productNotAvailable.Add(productId);
                            }
                        }
                        //if in current order.ProductsId any unavailable products and order not submitted yet,
                        //recounts total price and updates order.ProductsId
                        if (productNotAvailable.Count != 0 && !order.SubmittedOrder)
                        {
                            decimal priceUpdate = 0;
                            order.ProductsId.Clear();
                            foreach (Product product in productsAvailable)
                            {
                                order.ProductsId.Add(product.Id!);
                                priceUpdate += product.Price;
                            }

                            order.TotalPrice = priceUpdate;
                            _dbContext.Orders.Update(order);
                            var result = await _dbContext.SaveChangesAsync();

                            if (result == 0)
                            {
                               string message = "Some inner error occured. Unable to process your request. Please tyr latter or contact to supporting team.";
                                return Warning(message, 0);
                            }
                            info = MessageUnavailableProducts(productNotAvailable, order.SubmittedOrder);
                        } 

                        return Successfull(new OrderDisplayModel(order, info));
                    }
                    else
                    {
                        return Warning("Order with Id: " + orderId + ", was not found. Access declined at: " + DateTime.Now, (int)HttpStatusCode.NotFound);
                    }
                }
                else
                {
                    return Warning("User was not found in system, please ensure that you signed in", (int)HttpStatusCode.BadRequest);
                }
            }
            catch(Exception ex)
            {
                return Error(ex);
            }
        }

        //Gets all orders for currentlly signed in account
        [HttpGet, Route("/order/all")]
        public async Task<ActionResult<List<OrderDisplayModel>>> GetAllOrders()
        {
            try
            {
                var user = await _userManager.FindByNameAsync(User.Identity!.Name!);
                user ??= await _userManager.FindByEmailAsync(User.Identity!.Name!);

                if (user is not null)
                {
                    List<Order> orders = await _dbContext.Orders.Where(_ => _.UserId.Equals(user.Id)).ToListAsync();

                    List<OrderDisplayModel> model = [];

                    orders?.ForEach(order => model.Add(new(order, "")));

                    return model.Count != 0 ? Successfull(model) :
                        Warning("There nor orders found for current user.", (int)HttpStatusCode.NoContent);
                }
                else
                {
                    return Warning("Please sign in, Unauthorized access declined.", (int)HttpStatusCode.Unauthorized);
                }
            }
            catch (Exception ex)
            {
                return Error(ex);
            }
        }
        //Deleting any order
        [HttpDelete, Route("/order/delete")]
        [Authorize(Roles = ApiConstants.Admin)]
        public async Task<ActionResult<OrderDisplayModel>> RemoveOrder([FromQuery]string orderId)
        {
            try
            {
                HttpContextAccessor accessor = new();
                if (accessor.HttpContext!.User.IsInRole(ApiConstants.Admin))
                {
                    Order? order = await _dbContext.Orders.FirstOrDefaultAsync(_ => _.OrderId.Equals(orderId));

                    if(order is not null)
                    {
                        _dbContext.Remove(order);
                        var result = await _dbContext.SaveChangesAsync();
                        return result == 0 ? 
                            Warning("Unable to process your request for order ID: " + orderId, 0) :
                            Successfull(new OrderDisplayModel(order, "The Order Deleted successfully."));
                    }
                    else
                    {
                        return Warning("Order with ID: " + orderId + ", was not found.", (int)HttpStatusCode.NotFound);
                    }
                }
                else
                {
                    return Warning("Unauthorized access declined at: " + DateTime.Now, (int)HttpStatusCode.Unauthorized);
                }
            }
            catch (Exception ex)
            {
                return Error(ex);
            }
        }

        [HttpGet, Route("/order/is-any")]
        [Authorize(Roles = ApiConstants.Admin)]
        public async Task<ActionResult<bool>> IsAnyUnsubmitted()
        {
            try
            {
                var user = await _userManager.FindByNameAsync(User.Identity!.Name!);
                user ??= await _userManager.FindByEmailAsync(User.Identity!.Name!);
                if(user is not null)
                {
                    bool result = await _dbContext.Orders.Where(_ => _.UserId.Equals(user.Id) && !_.SubmittedOrder).AnyAsync();
                    return Successfull(result);
                }
                else
                {
                    return Warning("User was not found in system, please ensure that you signed in", (int)HttpStatusCode.BadRequest);
                }
            }
            catch(Exception ex)
            {
                return Error(ex);
            }
        }

        [HttpGet, Route("/order/current")]
        [Authorize(Roles = ApiConstants.Admin)]
        public async Task<ActionResult<OrderDisplayModel>> GetCurrentOrder()
        {
            try
            {
                var user = await _userManager.FindByNameAsync(User.Identity!.Name!);
                user ??= await _userManager.FindByEmailAsync(User.Identity!.Name!);

                if(user is not null)
                {
                    Order? order = await _dbContext.Orders.Where(_ => _!.UserId!.Equals(user!.Id) && !_.SubmittedOrder).FirstOrDefaultAsync();
                    if (order is not null)
                    {
                       return Successfull(new OrderDisplayModel(order, ""));
                    }
                    else
                    {
                        Warning("There nor unsubmitted orders found for current user.", (int)HttpStatusCode.NoContent);
                    }
                }
                else
                {
                    return Warning("User was not found in system, please ensure that you signed in", (int)HttpStatusCode.BadRequest);
                }
                return Warning("User was not found in system, please ensure that you signed in", (int)HttpStatusCode.BadRequest);
            }
            catch(Exception ex)
            {
               return Error(ex);
            }
        }
    }

    [ApiController]
    [ApiVersion("2")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [Authorize]
    [EnableCors(PolicyName = ApiConstants.CorsNameUser)]
    [Route("account/")]
    public class OrderV2Controller(ILogger<OrderV2Controller> logger, UserManager<ApiUser> userManager, StockDBServices stockServices, AuthenticationApiDbContext dbContext) : ControllerBase
    {
        private readonly ILogger<OrderV2Controller> _logger = logger;
        private readonly UserManager<ApiUser> _userManager = userManager;
        private readonly StockDBServices _stockServices = stockServices;
        private readonly AuthenticationApiDbContext _dbContext = dbContext;

        #region of Help Methods
        private void LogingError(Exception error) => _logger.LogError(message: error.Message, args: error.StackTrace);
        private void LogingInformation(string message) => _logger.LogInformation(message: message);
        private void LogingWarning(string message) => _logger.LogWarning(message: message);
        private ActionResult Warning(string message, int statusCode)
        {
            OrderDisplayModel model = new(new Order(), message);

            LogingWarning(message);
            return statusCode == (int)HttpStatusCode.Unauthorized ?
                Unauthorized(model) :
                statusCode == (int)HttpStatusCode.NotFound ?
                NotFound(model) :
                statusCode == (int)HttpStatusCode.BadRequest ?
                BadRequest(model) :
                UnprocessableEntity(model);
        }
        private ActionResult Successfull(OrderDisplayModel model)
        {
            LogingInformation(model.ToJson());
            return Ok(model);
        }
        private ActionResult Successfull(List<OrderDisplayModel> orders)
        {
            LogingInformation(orders.ToJson());
            return Ok(orders);
        }
        private ActionResult Successfull(bool data)
        {
            return Ok(data);
        }
        private ActionResult Error(Exception ex)
        {
            LogingError(ex);
            return Problem(ex.Message);
        }

        private string MessageUnavailableProducts(List<string> productsIdsNotFound, bool orderIsSubmitted)
        {
            string message = "";
            if (productsIdsNotFound.Count != 0)
            {
                message += "Some product from your order, currently unavailable in stock.";
                message += orderIsSubmitted ?
                    "There no detailed data can be displayed for products with IDs: " :
                    "Total Order price recounted and products was removed from your Order." +
                    "Removed Products IDs: ";
                int index = productsIdsNotFound.Count;
                foreach (string id in productsIdsNotFound)
                {
                    --index;
                    message += index != 0 ? id + ", " : id + ".";
                }
            }
            return message;
        }
        #endregion

        //Creates new order, if there is not unsubmitted orders, for the current authorized user,
        //else returns Problem() object result with information message that contains
        //id for unsubmitted order.
        //User, should add new products to last not submitted order and submit it,
        //before to start another new order.
        [HttpPost, Route("/order")]
        public async Task<ActionResult<OrderDisplayModel>> PostOrder([FromQuery] List<string> productsIds)
        {
            try
            {
                string message = "";
                //checks if user is signed in to create order
                var user = await _userManager.FindByNameAsync(User.Identity!.Name!);
                user ??= await _userManager.FindByEmailAsync(User.Identity!.Name!);
                if (user is not null)
                {
                    var listOfOrders = await _dbContext.Orders.Where(_ => _.UserId.Equals(user!.Id)).ToListAsync();
                    //checks if user has an uncompleted orders before to create new order
                    if (listOfOrders.Any(_ => !_.SubmittedOrder))
                    {
                        return Warning("OrderId: " + listOfOrders.FirstOrDefault(_ => !_.SubmittedOrder!)!.OrderId +
                            ". Please submit order above before to create new order", 0);
                    }

                    Order orderToPost = new()
                    {
                        OrderId = Guid.NewGuid().ToString(),
                        User = user,
                        UserId = user.Id,
                        TotalPrice = 0,
                        OrderDateTime = DateTime.Now,
                        SubmittedOrder = false
                    };
                    //checks if provided products ids exists and if they are available in stock
                    //adds the ids of products to the order
                    foreach (string id in productsIds)
                    {
                        Product product = await _stockServices.GetBookByIdAsync(id);

                        if (product?.IsAvailable == true)
                        {
                            orderToPost.ProductsId!.Add(product.Id!);
                            orderToPost.TotalPrice += product.Price;
                        }
                        else
                        {
                            message += "The product with ID: " + id;
                            message += product is null ?
                                ", was not found in stock, please check if ID is correct." :
                                ", currently is unavailble.";
                            return Warning(message + " Unable to process your order.", (int)HttpStatusCode.NotFound);
                        }
                    }

                    _dbContext.Orders.Add(orderToPost);
                    var result = await _dbContext.SaveChangesAsync();

                    if (result == 0)
                    {
                        return Warning("Unable to process request. Order was not saved.", (int)HttpStatusCode.BadRequest);
                    }
                    else
                    {
                        return Successfull(new OrderDisplayModel(orderToPost,
                            "Order created successfully"));
                    }

                }
                return Warning("User was not found in system, please ensure that you signed in",
                    (int)HttpStatusCode.BadRequest);
            }
            catch (Exception ex)
            {
                return Error(ex);
            }
        }

        //Adds more products to existing order for the current authorized user
        [HttpPut, Route("/order/products/add")]
        public async Task<ActionResult<OrderDisplayModel>> PutOrderAddProducts([FromQuery] List<string> productsIds, [Required] string orderId)
        {
            try
            {
                var user = await _userManager.FindByNameAsync(User.Identity!.Name!);
                user ??= await _userManager.FindByEmailAsync(User.Identity!.Name!);
                string message = "";
                if (user is not null)
                {
                    Order? order = await _dbContext.Orders.FirstOrDefaultAsync(order => order.UserId.Equals(user.Id) && order.OrderId.Equals(orderId));

                    //Checks if order with requested id exists and is not submitted yet
                    if (order?.SubmittedOrder == false)
                    {
                        //Checks if previously added products in order are still available
                        List<string> productsNotAvailable = [];
                        foreach (string id in order.ProductsId!)
                        {
                            Product product = await _stockServices.GetBookByIdAsync(id);
                            if (product is null || !product.IsAvailable)
                            {
                                productsNotAvailable.Add(id);
                            }
                        }
                        if (productsNotAvailable.Count != 0)
                        {
                            message += MessageUnavailableProducts(productsNotAvailable, order.SubmittedOrder);

                            order.ProductsId =
                                [.. order.ProductsId.Where(id => !productsNotAvailable.Contains(id))];
                        }
                        //Checks if currently requested products are in stock
                        foreach (string id in productsIds)
                        {
                            Product product = await _stockServices.GetBookByIdAsync(id);

                            if (product?.IsAvailable == true)
                            {
                                order.ProductsId!.Add(product.Id!);
                            }
                            else
                            {
                                message += "The product with ID: " + id + ", was not added to the order, because product ";
                                message += product is null ?
                                    " was not found in stock, please check if ID is correct." :
                                    " currently is unavailble.";
                            }
                        }
                        //recounts/resets ordr total price
                        decimal price = 0;
                        foreach (string id in order.ProductsId)
                        {
                            price += (await _stockServices.GetBookByIdAsync(id)).Price;
                        }
                        order.TotalPrice = price;
                        order.OrderDateTime = DateTime.Now;
                        _dbContext.Orders.Update(order);
                        var result = await _dbContext.SaveChangesAsync();

                        if (result == 0)
                        {
                            return Warning("Unable to process request. Products was not added",
                                (int)HttpStatusCode.BadRequest);
                        }
                        else return Successfull(new OrderDisplayModel(order,
                            " Order updated successfully"));
                    }
                    else
                    {
                        message = "Sorry, the requested order, with id: " + orderId;
                        message += order is not null ?
                         ", already submitted. Please start new order." :
                         ", was not found. Please start new order.";
                        return Warning(message + "Request declined at: " + DateTime.Now, (int)HttpStatusCode.BadRequest);
                    }
                }
                else
                {
                    return Warning("User was not found in system, please ensure that you signed in", (int)HttpStatusCode.BadRequest);
                }
            }
            catch (Exception ex)
            {
                return Error(ex);
            }
        }

        //Deletes products from existing order for the current authorized user
        [HttpPut, Route("/order/products/delete")]
        public async Task<ActionResult> PutOrderDeleteProducts([FromQuery] List<string> productIds, [Required] string orderId)
        {
            try
            {
                var user = await _userManager.FindByNameAsync(User.Identity!.Name!);
                user ??= await _userManager.FindByEmailAsync(User.Identity!.Name!);
                string message = "";
                if (user is not null)
                {
                    Order? order = await _dbContext.Orders.FirstOrDefaultAsync(order => order.UserId.Equals(user.Id) && order.OrderId.Equals(orderId));

                    //Checks if order with requested id exists and is not submitted yet
                    if (order?.SubmittedOrder == false)
                    {
                        foreach (string id in productIds)
                        {
                            if (order.ProductsId!.Contains(id))
                            {
                                order.ProductsId.Remove(id);
                                order.TotalPrice -= (await _stockServices.GetBookByIdAsync(id)).Price;
                            }
                        }
                        order.OrderDateTime = DateTime.Now;
                        _dbContext.Orders.Update(order);
                        var result = await _dbContext.SaveChangesAsync();

                        string info = "OrderID: " + order.OrderId + ", at DateTime: " + order.OrderDateTime;

                        if (result == 0)
                        {
                            return Warning("Unable to process request. Products was not removed " +
                                info, (int)HttpStatusCode.BadRequest);
                        }
                        else return Successfull(new OrderDisplayModel(order ,"Products removed successfully, " + info));
                    }
                    else
                    {
                        message = "Sorry, the requested order, with id: " + orderId;
                        message += order is not null ?
                         ", already submitted. Please start new order." :
                         ", was not found. Please start new order.";
                        return Warning(message + "Request declined at: " + DateTime.Now, (int)HttpStatusCode.BadRequest);
                    }
                }
                else
                {
                    return Warning("User was not found in system, please ensure that you signed in", (int)HttpStatusCode.BadRequest);
                }
            }
            catch (Exception ex)
            {
                return Error(ex);
            }
        }

        //Submitts orders
        //Checks if there all products still available in stock before submmiting.
        //If some products is not available, then deletes it from order, recounting total price and
        //informing the user about this and that the order needs to be double-checked and resubmit it
        [HttpPut, Route("/order/submit")]
        public async Task<ActionResult> PutOrderAsSubmitted([FromQuery] string orderId)
        {
            try
            {
                var user = await _userManager.FindByNameAsync(User.Identity!.Name!);
                user ??= await _userManager.FindByEmailAsync(User.Identity!.Name!);
                string message = "";
                if (user is not null)
                {
                    Order? order = await _dbContext.Orders.FirstOrDefaultAsync(order => order.UserId.Equals(user.Id) && order.OrderId.Equals(orderId));

                    //Checks if order with requested id exists and is not submitted yet
                    if (order?.SubmittedOrder == false)
                    {
                        //Checks if all products in order exists and available
                        if (order.ProductsId!.Count != 0)
                        {
                            string idsDeleted = "";
                            List<string> productsIds = [.. order.ProductsId!];
                            List<string> productsIdNotAvailable = [];
                            foreach (string productId in productsIds)
                            {
                                Product product = await _stockServices.GetBookByIdAsync(productId);
                                if (product is null || !product.IsAvailable)
                                {
                                    idsDeleted += productId + ", ";
                                    productsIdNotAvailable.Add(productId);
                                    order.ProductsId!.Remove(productId);
                                }
                            }
                            //Recounts total price for order if there was removed any not in stock products
                            if (productsIdNotAvailable.Count != 0)
                            {
                                decimal totalPrice = 0;
                                foreach (string producId in productsIds)
                                {
                                    Product product = await _stockServices.GetBookByIdAsync(producId);
                                    totalPrice = product is not null && product.IsAvailable ?
                                        totalPrice + product.Price : totalPrice + 0;
                                }
                                order.TotalPrice = totalPrice;
                            }
                            //Resetting order 
                            order.SubmittedOrder = productsIdNotAvailable.Count == 0;
                            order.OrderDateTime = DateTime.Now;
                            _dbContext.Orders.Update(order);

                            var result = await _dbContext.SaveChangesAsync();

                            //Checks the result 
                            if (result == 0)
                            {
                                LogingWarning("Unable to process request. Order was not saved, OrderID" + order.OrderId);
                                return BadRequest("Not able to process your request. Order was not saved.");
                            }
                            else
                            {
                                //if there was deleted any products from Order, needs user to recheck what is
                                //in it before submitting
                                if (!order.SubmittedOrder)
                                {
                                    message = "The product/products with id: " + idsDeleted + ", was not found or        currently unavailable. Those products was removed from your order. Order was not subbmitted. Please recheck the order and resubmit it again.";
                                    return Warning(message,(int)HttpStatusCode.NotFound);
                                }
                                else
                                {
                                    message = "Order submitted successfully, at: " + order.OrderDateTime;
                                    return Successfull(new OrderDisplayModel(order, message));
                                }
                            }
                        }
                        else
                        {
                            return Warning("Can not submit empty Order. Order should have at list one product. Request declined at: " + DateTime.Now,
                                (int)HttpStatusCode.BadRequest);
                        }
                    }
                    else
                    {
                        message = "Sorry, the requested order, with id: " + orderId;
                        message += order is not null ?
                         ", already submitted." :
                         ", was not found.";
                        return Warning(message + "Request declined at: " + DateTime.Now,
                            (int)HttpStatusCode.BadRequest);
                    }
                }
                else
                {
                    return Warning("User was not found in system, please ensure that you signed in",
                        (int)HttpStatusCode.BadRequest);
                }
            }
            catch (Exception ex)
            {
                return Error(ex);
            }
        }

        //Checks before if current user has authority to access to the requested order
        [HttpGet, Route("/order/details")]
        public async Task<ActionResult<OrderDisplayModel>> GetOrder([FromQuery] string orderId)
        {
            try
            {
                string info = "";
                var user = await _userManager.FindByNameAsync(User.Identity!.Name!);
                user ??= await _userManager.FindByEmailAsync(User.Identity!.Name!);
                if (user is not null)
                {
                    Order? order = await _dbContext.Orders.FirstOrDefaultAsync(order => order.UserId.Equals(user.Id) && order.OrderId.Equals(orderId));
                    if (order is not null)
                    {
                        List<Product> productsAvailable = [];
                        List<string> productNotAvailable = [];
                        foreach (string productId in order.ProductsId!)
                        {
                            Product product = await _stockServices.GetBookByIdAsync(productId);
                            if (product?.IsAvailable == true)
                            {
                                productsAvailable.Add(product);
                            }
                            else
                            {
                                productNotAvailable.Add(productId);
                            }
                        }
                        //if in current order.ProductsId any unavailable products and order not submitted yet,
                        //recounts total price and updates order.ProductsId
                        if (productNotAvailable.Count != 0 && !order.SubmittedOrder)
                        {
                            decimal priceUpdate = 0;
                            order.ProductsId.Clear();
                            foreach (Product product in productsAvailable)
                            {
                                order.ProductsId.Add(product.Id!);
                                priceUpdate += product.Price;
                            }

                            order.TotalPrice = priceUpdate;
                            _dbContext.Orders.Update(order);
                            var result = await _dbContext.SaveChangesAsync();

                            if (result == 0)
                            {
                                string message = "Some inner error occured. Unable to process your request. Please tyr latter or contact to supporting team.";
                                return Warning(message, 0);
                            }
                            info = MessageUnavailableProducts(productNotAvailable, order.SubmittedOrder);
                        }

                        return Successfull(new OrderDisplayModel(order, info));
                    }
                    else
                    {
                        return Warning("Order with Id: " + orderId + ", was not found for current SignedIn user. The access declined at: " + DateTime.Now, (int)HttpStatusCode.NotFound);
                    }
                }
                else
                {
                    return Warning("User was not found in system, please ensure that you signed in", 
                        (int)HttpStatusCode.BadRequest);
                }
            }
            catch (Exception ex)
            {
                return Error(ex);
            }
        }

        //Gets all orders for currentlly signed in account
        [HttpGet, Route("/order/all")]
        public async Task<ActionResult<List<OrderDisplayModel>>> GetAllOrders()
        {
            try
            {
                var user = await _userManager.FindByNameAsync(User.Identity!.Name!);
                user ??= await _userManager.FindByEmailAsync(User.Identity!.Name!);
                if (user is not null)
                {
                    List<Order> orders = await _dbContext.Orders.Where(_ => _.UserId.Equals(user.Id)).ToListAsync();

                    List<OrderDisplayModel> model = [];

                    orders?.ForEach(order => model.Add(new(order, "")));

                    return model.Count != 0 ? Successfull(model) :
                        Warning("There nor orders found for current user", (int)HttpStatusCode.NoContent);
                }
                else
                {
                    return Warning("Please sign in, Unauthorized access declined.", (int)HttpStatusCode.Unauthorized);
                }
            }
            catch (Exception ex)
            {
                return Error(ex);
            }
        }

        [HttpGet, Route("/order/is-any")]
        public async Task<ActionResult<bool>> IsAnyUnsubmitted()
        {
            try
            {
                var user = await _userManager.FindByNameAsync(User.Identity!.Name!);
                user ??= await _userManager.FindByEmailAsync(User.Identity!.Name!);
                if (user is not null)
                {
                    bool result = await _dbContext.Orders.Where(_ => _.UserId.Equals(user.Id) && !_.SubmittedOrder).AnyAsync();
                    return Successfull(result);
                }
                else
                {
                    return Warning("User was not found in system, please ensure that you signed in", (int)HttpStatusCode.BadRequest);
                }
            }
            catch (Exception ex)
            {
                return Error(ex);
            }
        }

        [HttpGet, Route("/order/current")]
        public async Task<ActionResult<OrderDisplayModel>> GetCurrentOrder()
        {
            try
            {
                var user = await _userManager.FindByNameAsync(User.Identity!.Name!);
                user ??= await _userManager.FindByEmailAsync(User.Identity!.Name!);

                if (user is not null)
                {
                    Order? order = await _dbContext.Orders.Where(_ => _!.UserId!.Equals(user!.Id) && !_.SubmittedOrder).FirstOrDefaultAsync() ;
                    return order is not null ?
                        Successfull(new OrderDisplayModel(order, "")) :
                        Warning("There nor unsubmitted orders found for current user.", (int)HttpStatusCode.NoContent);
                }
                else
                {
                    return Warning("User was not found in system, please ensure that you signed in", (int)HttpStatusCode.BadRequest);
                }
            }
            catch (Exception ex)
            {
                return Error(ex);
            }
        }
    }
}
