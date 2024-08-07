﻿using Asp.Versioning;
using BookShop.API.Controllers.Services;
using BookShop.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using System.ComponentModel.DataAnnotations;
using System.Net;

namespace BookShop.API.Controllers
{
    /*
     * Version for Admin to retrive all data and manipulates with data
     * Supports all methods: HttpGet, HttpPost, HttpPut and HttpDelete
     */
    [ApiController]
    [ApiVersion("1")]
    [Authorize(Roles = ApiConstants.Admin)]
    [EnableCors(PolicyName = ApiConstants.CorsNameAdmin)]
    [Produces("application/json")]
    [Consumes("application/json")]
    public class StockV1Controller(StockDBServices services, ILogger<StockV1Controller> logger) : ControllerBase
    {
        private readonly StockDBServices _services = services;
        private readonly ILogger<StockV1Controller> _logger = logger;

        #region of HttpGet Methods
        #region of simple HttpGet Methods
        /*!ATTENTION! may be overflow error, or slowdown preformance.Depends on current size of database
         * returns all data from database collection
        */
        [HttpGet, Route("books/all")]
        public async Task<ActionResult<List<Product>>> GetAllProducts()
        {
            try
            {
                var products = await _services.GetAllBooksAsync();

                return products is null || products.Count == 0 ?
                    NotFound() : Ok(products);
            }
            catch (Exception ex)
            {
                LoggError(ex.Message.ToString(), ex.StackTrace!);
                return Problem(ex.Message);
            }
        }

        //returns list of requested quantity of products for requested page
        //list sorted in requested order, by requested parameter(by Title or by Author or by Price)
        //if not specified sorting order && parameter for order, then its uses default parameters:
        //descending order by Price
        [HttpGet, Route("books/page")]
        public async Task<ActionResult<List<Product>>> GetPerPageProducts([FromQuery] PageModel model)
        {
            try
            {
                Query query = new(model.RequestedPage, model.QuantityPerPage, (int)GetQuantityAllProducts().Result);
                var products = (await _services.GetBooksInOrder(model.InAscendingOrder, model.OrderBy)).Skip(query.QuantityToSkip).Take(query.RequestedQuantity);

                return products is null || !products.Any() ?
                    NotFound("There no products found under entered requirements") : Ok(products);
            }
            catch (Exception ex)
            {
                LoggError(ex.Message.ToString(), ex.StackTrace!);
                return Problem(ex.Message);
            }
        }

        [HttpGet, Route("book/id")]
        public async Task<ActionResult<Product>> GetProductById([FromQuery]string id)
        {
            try
            {
                var product = await _services.GetBookByIdAsync(id);
                return product is null ?
                    NotFound("There no product found under entered requirements") : 
                    Ok(product);
            }
            catch(Exception ex)
            {
                LoggError(ex.Message.ToString(), ex.StackTrace!);
                return Problem(ex.Message);
            }
        }

        //gets a list of Product, where Product.IsAvailable == isAvailable
        [HttpGet, Route("books/available")]
        public async Task<ActionResult<List<Product>>> GetProductAvailable([FromQuery]bool isAvailable)
        {
            try
            {
                var products = (await _services.GetAllBooksAsync()).Where(_ => _.IsAvailable == isAvailable).ToList();
                return products.Count != 0 ? Ok(products) : NotFound("Nor data under requested conditions to display");
            }
            catch(Exception ex)
            {
                LoggError(ex.Message, ex.StackTrace!);
                return Problem(ex.Message);
            }

        }

        //gets a list of Product, where Product.IsAvailable == isAvailable, at requested page
        //minimum quantity per page = 5, maximum quantity per page = 30
        [HttpGet, Route("books/available/page")]
        public async Task<ActionResult<List<Product>>> GetPageProductsAvailable([FromQuery]bool isAvailable, [FromQuery]PageModel model)
        {
            try
            {
                Query query = new(model.RequestedPage, model.QuantityPerPage, (int)GetQuantityAvailable(isAvailable).Result);

                var products = (await _services.GetAllBooksAsync()).Where(_ => _.IsAvailable == isAvailable);
                List<Product> result = OrderBy(model, [..products]);
                
                if(result.Count != 0)
                {
                    result = [.. result.Skip(query.QuantityToSkip).Take(query.RequestedQuantity)];
                    return Ok(result);
                }
                return NotFound("There no products found under entered requirements");
            }
            catch(Exception ex)
            {
                LoggError(ex.Message, ex.StackTrace!);
                return Problem(ex.Message);
            }
        }

        //return all genres in database
        [HttpGet, Route("books/genres")]
        public async Task<ActionResult<List<string>>> GetAllGenres()
        {
            try
            {
                var products = await _services.GetAllBooksAsync();
                if (products.Count != 0)
                {
                    List<string> genres = [];

                    foreach(var product in products)
                    {
                        if (product.Genres.Length != 0)
                        { 
                            genres.AddRange(product.Genres.Where(g => !genres.Any(_ => _.Trim().Equals(g.Trim(), StringComparison.OrdinalIgnoreCase))));
                        }                      
                    }
                    return genres.Count != 0 ? Ok(genres) : NotFound("No record found");
                }
                else return NotFound("No record found");
            }
            catch(Exception ex)
            {
                LoggError(ex.Message, ex.StackTrace!);
                return NotFound("No records found");
            }
        }

        //returns all books in genre
        [HttpGet, Route("books/genre")]
        public async Task<ActionResult<List<Product>>> GetProductsInGenre([FromQuery] string genre, [FromQuery] PageModel model)
        {
            try
            {
                Query query = new(model.RequestedPage, model.QuantityPerPage, GetQuantityInGenre(genre).Result);

                var products = (await _services.GetAllBooksAsync()).Where(_ => _.Genres.Contains(genre, StringComparer.OrdinalIgnoreCase));

                List<Product> result = OrderBy(model, [.. products]);
                return result.Count != 0 ?
                     Ok(result.Skip(query.QuantityToSkip).Take(query.RequestedQuantity)) :
                     NotFound("No record found under requsted condition");
                
            }
            catch (Exception ex)
            {
                LoggError(ex.Message, ex.StackTrace!);
                return Problem(ex.Message);
            }
        }
        #endregion
        #region of Filtering Methods
        //returns list of ALL books where Title,Author, Language or one of item from array of Genres EQUALS to searchCondition
        [HttpGet, Route("books/filter/condition/equals")]
        public async Task<ActionResult<List<Product>>> GetProductsEqualsCondition([FromQuery]string condition, [FromQuery]PageModel model)
        {
            try
            {
                var products = await _services.GetBooksEqualsConditionAsync(condition, model.InAscendingOrder, model.OrderBy);

                Query query = new(model.RequestedPage, model.QuantityPerPage, products.Count);

                return products.Count != 0 ?
                    Ok(products.Skip(query.QuantityToSkip).Take(query.RequestedQuantity)) :
                    NotFound("There nor records found under requested condition");
            }
            catch(Exception ex)
            {
                LoggError(ex.Message, ex.StackTrace!);
                return Problem(ex.Message);
            }
        }
        //returns list of ALL books where Title,Author, Language or one of item from array of Genres CONTAINS to searchCondition
        [HttpGet, Route("books/filter/condition/contains")]
        public async Task<ActionResult<List<Product>>> GetProductsContainsCondition([FromQuery] string condition, [FromQuery] PageModel model)
        {
            try
            {
                var products = await _services.GetBooksContainsConditionAsync(condition, model.InAscendingOrder, model.OrderBy);

                Query query = new(model.RequestedPage, model.QuantityPerPage, products.Count);

                return products.Count != 0 ?
                    Ok(products.Skip(query.QuantityToSkip).Take(query.RequestedQuantity)) :
                    NotFound("There nor records found under requested condition");
            }
            catch (Exception ex)
            {
                LoggError(ex.Message, ex.StackTrace!);
                return Problem(ex.Message);
            }
        }

        //Filters the list by each provided parameter in FilterProducts
        //Exception if FilterProducts.IsAvailable was not specified, then its sets as false
        //Devides content into pages by parameters requested or auto from PageModel
        [HttpGet, Route("books/filter/")]
        public async Task<ActionResult<List<Product>>> GetProductsFilter([FromQuery]FilterProducts filter, [FromQuery]PageModel model)
        {
            try
            {
                var products = await _services.GetBooksInOrder(model.InAscendingOrder, model.OrderBy);
                List<Product> result = ApplyFilters(products, filter);
                result = OrderBy(model, result);
                if (result.Count != 0)
                {
                    Query query = new(model.RequestedPage, model.QuantityPerPage, result.Count);
                    result = [.. result.Skip(query.QuantityToSkip).Take(query.RequestedQuantity)];
                }
                
                return result.Count != 0 ? 
                        Ok(result) : 
                        NotFound("Nor records found under requested condition");
            }
            catch(Exception ex)
            {
                LoggError(ex.Message, ex.StackTrace!);
                return Problem(ex.Message);
            }
        }
        #endregion
        #endregion
        #region of HttpMethods for manipulations with Collection
        [HttpPost, Route("book/add")]
        public async Task<ActionResult> PostProduct([FromQuery]Product product)
        {
            try
            {
                if(product.Genres is null || product.Genres.Length == 0)
                {
                    product.Genres = ["unspecified"];
                }
                Product book = new()
                {
                    Title = product.Title,
                    Author = product.Author,
                    Annotation = product.Annotation,
                    IsAvailable = product.IsAvailable,
                    Language = product.Language,
                    Link = !string.IsNullOrEmpty(product.Link.ToString()) && Uri.IsWellFormedUriString(product.Link.ToString(), UriKind.Absolute) ?
                    product.Link : new Uri("about:blank"),
                    Genres = [.. product.Genres],
                    Price = product.Price > 0 ? product.Price : 0
                };
                await _services.AddNewAsync(book);

                LoggInfo(Ok().StatusCode, "Successfully added product, with id: '" + book.Id + "'. ");
                return Ok("Successfully added: " + book.ToJson());
            }
            catch (Exception ex)
            {
                LoggError(ex.Message.ToString(), ex.StackTrace!);
                return Problem(ex.Message);
            }
        }

        [HttpDelete, Route("book/delete")]
        public async Task<ActionResult> DeleteProduct([Required]string id)
        {
            try
            {
                Product book = await _services.GetBookByIdAsync(id);
                if(book is not null)
                {
                    await _services.DeleteOneAsync(id);
                    return Ok("Deleted successfuly: " + book.ToJson());
                }
                else
                {
                    return Warning("There nor book with ID: " + id + ", found. Requested process declined at: " + DateTime.Now, (int)HttpStatusCode.NotFound);
                }
            }
            catch(Exception ex)
            {
                return LoggError(ex.Message, ex.StackTrace!);
            }
        }

        [HttpPut, Route("book/update")]
        public async Task<ActionResult<Product>> PutProduct([FromQuery]Product book)
        {
            try
            {
                Product bookInDb = await _services.GetBookByIdAsync(book.Id!);

                if(bookInDb is not null)
                {
                    book.Title ??= bookInDb.Title;
                    book.Author ??= bookInDb.Author;
                    book.Annotation ??= bookInDb.Annotation;
                    book.Language ??= bookInDb.Language;

                    if(book.Genres.Contains("undefined") && !bookInDb.Genres.Contains("undefined"))
                    {
                        book.Genres = [ ..bookInDb.Genres];
                    }

                    book.Price = book.Price != 0 ? book.Price : bookInDb.Price;
                    book.Link = book.Link is not null && Uri.IsWellFormedUriString(book.Link.ToString(), UriKind.Absolute) ? book.Link :
                        bookInDb.Link is not null && Uri.IsWellFormedUriString(bookInDb.Link.ToString(), UriKind.Absolute) ? bookInDb.Link : new Uri("about:blank");

                    await _services.UpdateNewAsync(book);

                    LoggInfo((int)HttpStatusCode.OK, "Successfully updated: " + book.Id);
                    return Ok(book.ToJson());
                }
                else
                {
                    return book.Id is null ? 
                        Warning("Requires existing product ID for update request", (int)HttpStatusCode.BadRequest):
                        Warning("Product with ID: " + book.Id + ", was not found in stock.", (int)HttpStatusCode.NotFound);
                }
            }
            catch(Exception ex)
            {
                return LoggError(ex.Message, ex.StackTrace!);
            }
        }
        #endregion
        #region of Help Methods
        private ActionResult LoggError(string errorMessage, string errorStackTrace )
        {
            _logger.LogError(message: errorMessage, args: errorStackTrace);
            return Problem(errorMessage.ToJson());
        }
        private void LoggInfo(int statusCode, string message)=>
            _logger.LogInformation(message: message + ", DateTime: {@DateTime}, StatusCode: {@statusCode}", DateTime.Now, statusCode);
        private void LogingWarning(int statusCode, string message) => 
            _logger.LogWarning(message: message + ", DateTime: {@DateTime}, StatusCode: {@statusCode}", DateTime.Now, statusCode);
        private ActionResult Warning(string message, int statusCode)
        {
            LogingWarning(statusCode, message);
            return statusCode == (int)HttpStatusCode.Unauthorized ?
                Unauthorized(message.ToJson()) :
                statusCode == (int)HttpStatusCode.NotFound ?
                NotFound(message.ToJson()) :
                statusCode == (int)HttpStatusCode.BadRequest ?
                BadRequest(message.ToJson()) :
                Problem(message.ToJson());
        }

        //Applyes FilterProducts to the List<Product>, and returns filtered list
        //Exception if FilterProducts.IsAvailable was not specified, then its sets as false
        private List<Product> ApplyFilters(List<Product> products, FilterProducts filter)
        {
            try
            {
                if (products.Count != 0)
                {
                    List<Product> filtered = [.. products];

                    filtered = [.. filtered.Where(product => product.IsAvailable == filter.IsAvailable)];

                    filtered = !string.IsNullOrEmpty(filter.Author) ?
                        [.. filtered.Where(product => product.Author!.Trim().Contains(filter.Author.Trim(), StringComparison.OrdinalIgnoreCase))] : filtered;

                    filtered = !string.IsNullOrEmpty(filter.Title) ?
                        [.. filtered.Where(product => product.Title!.Trim().Contains(filter.Title.Trim(), StringComparison.OrdinalIgnoreCase))] : filtered;

                    filtered = !string.IsNullOrEmpty(filter.Annotation) ?
                        [.. filtered.Where(product => product.Annotation!.Trim().Contains(filter.Annotation.Trim(), StringComparison.OrdinalIgnoreCase))] : filtered;

                    filtered = !string.IsNullOrEmpty(filter.Language) ?
                        [.. filtered.Where(product => product.Language!.Trim().Contains(filter.Language.Trim(), StringComparison.OrdinalIgnoreCase))] : filtered;

                    filtered = filter.Genres is not null ?
                        [.. filtered.Where(product => product.Genres!.Any(genre => filter.Genres!.Any(g => g.Contains(genre, StringComparison.OrdinalIgnoreCase))))] : filtered;

                    filtered = filter.MinPrice > 0 && filter.MaxPrice > 0 ?
                        [.. filtered.Where(product => product.Price >= filter.MinPrice && product.Price <= filter.MaxPrice)] :
                        filter.MinPrice > 0 ?
                        [.. filtered.Where(product => product.Price >= filter.MinPrice)] :
                        filter.MaxPrice > 0 ?
                        [.. filtered.Where(product => product.Price <= filter.MaxPrice)] :
                        filtered;

                    return filtered;
                }
                return [];
            }
            catch(Exception ex)
            {
                LoggError(ex.Message, ex.StackTrace!);
                return [];
            }
        }
        #region of Sorting Methods
        //MAY RETURN NULL LIST
        //returns sorted list
        private List<Product> OrderBy(PageModel model, List<Product> products)
        {
            try
            {
                if (products.Count != 0)
                {
                    List<Product> result = [];
                    if (model.InAscendingOrder)
                    {
                        result = model.OrderBy.Equals("author", StringComparison.OrdinalIgnoreCase) ?
                            [.. products.OrderBy(_ => _.Author)] :
                            model.OrderBy.Equals("title", StringComparison.OrdinalIgnoreCase) ?
                            [.. products.OrderBy(_ => _.Title)] :
                            [.. products.OrderBy(_ => _.Price)];
                    }
                    else
                    {
                        result = model.OrderBy.Equals("author", StringComparison.OrdinalIgnoreCase) ?
                            [.. products.OrderByDescending(_ => _.Author)] :
                            model.OrderBy.Equals("title", StringComparison.OrdinalIgnoreCase) ?
                            [.. products.OrderByDescending(_ => _.Title)] :
                            [.. products.OrderByDescending(_ => _.Price)];
                    }

                    return result;
                }
                return [];
            }
            catch(Exception ex)
            {
                LoggError(ex.Message, ex.StackTrace!);
                return [];
            }
        }
        #endregion
        #region of count methods
        //returns quantity of all products in database
        [HttpGet, Route("books/count/all")]
        public async Task<int> GetQuantityAllProducts()
        {
            try
            {
                int quantity = (await _services.GetAllBooksAsync()).Count;
                return quantity;
            }
            catch(Exception ex)
            {
                LoggError(ex.Message.ToString(), ex.StackTrace!);
                return 0;
            }
        }

        //returns quantity of products in database that Product.IsAvailable == available
        [HttpGet, Route("books/count/available")]
        public async Task<int> GetQuantityAvailable(bool available)
        {
            try
            {
                int quantity = (await _services.GetAllBooksAsync()).Where(_ => _.IsAvailable == available).Count();
                return quantity;
            }
            catch(Exception ex)
            {
                LoggError(ex.Message, ex.StackTrace!);
                return 0;
            }
        }

        [HttpGet, Route("books/count/ingenre")]
        public async Task<int> GetQuantityInGenre([FromQuery]string genre)
        {
            try
            {
                int quantity = (await _services.GetAllBooksAsync()).Where(_ => _.Genres.Contains(genre, StringComparer.OrdinalIgnoreCase)).Count();
                return quantity;
            }
            catch(Exception ex)
            {
                LoggError(ex.Message, ex.StackTrace!);
                return 0;
            }
        }

        //Returns total number of pages for method  GetProductsFilter()
        [HttpGet, Route("books/filter/count")]
        public async Task<ActionResult<Query>> CountPagesFilter([FromQuery] FilterProducts filter, [FromQuery] PageModel model)
        {
            try 
            {
                var products = await _services.GetBooksInOrder(model.InAscendingOrder, model.OrderBy);
                if (products.Count != 0)
                {
                    Query query = new(model.RequestedPage, model.QuantityPerPage, ApplyFilters(products, filter).Count);
                    return Ok(query);
                }
                return BadRequest("Unable to process your request");
            }
            catch (Exception ex)
            {
                LoggError(ex.Message, ex.StackTrace!);
                return Problem(ex.Message);
            }
        }

        [HttpGet, Route("books/filter/count/condition")]
        public async Task<int> CountPagesEqualsCondition([FromQuery] string condition, [FromQuery] PageModel model)
        {
            try
            {
                int quantity = (await _services.GetBooksContainsConditionAsync(condition, model.InAscendingOrder, model.OrderBy)).Count;
                return quantity;
            }
            catch(Exception ex)
            {
                LoggError(ex.Message, ex.StackTrace!);
                return 0;
            }
        }
        #endregion
        #endregion
    }

    /*
     * Version for Users to only retrive some data from database, where property 'available' == true
     * Supports only methods: HttpGet
     */
    [ApiController]
    [ApiVersion("2")]
    [Authorize]
    [EnableCors(PolicyName = ApiConstants.CorsNameUser)]
    [Produces("application/json")]
    [Consumes("application/json")]
    public class StockV2Controller(StockDBServices services, ILogger<StockV2Controller> logger) : ControllerBase
    {
        private readonly StockDBServices _services = services;
        private readonly ILogger<StockV2Controller> _logger = logger;

        #region of HttpGet Methods
        #region of simple HttpGet Methods
        /*!ATTENTION! may be overflow error, or slowdown preformance.Depends on current size of database
         * returns all data from database collection
        */
        [HttpGet, Route("books/all")]
        public async Task<ActionResult<List<Product>>> GetAllProducts()
        {
            try
            {
                var products = (await _services.GetAllBooksAsync()).Where(_ => _.IsAvailable).ToList();

                if(products is null)
                {
                    LoggInfo(NotFound().StatusCode, "No data in database found with specified requerments");
                }
                return products is null || products.Count == 0 ?
                    NotFound() : Ok(products);
            }
            catch (Exception ex)
            {
                LoggError(ex.Message, ex.StackTrace!);
                return Problem(ex.Message);
            }
        }

        //returns list of requested quantity of products.isAvailable for requested page
        //list sorted in requested order, by requested parameter(by Title or by Author or by Price)
        //if not specified sorting order && parameter for order, then its uses default parameters:
        //descending order by Price
        [HttpGet, Route("books/page")]
        public async Task<ActionResult<List<Product>>> GetPerPageProducts([FromQuery] PageModel model)
        {
            try
            {
                Query query = new(model.RequestedPage, model.QuantityPerPage, (int)GetQuantityAllProducts().Result);
                var products = (await _services.GetBooksInOrder(model.InAscendingOrder, model.OrderBy))
                    .Where(_ => _.IsAvailable)
                    .Skip(query.QuantityToSkip).Take(query.RequestedQuantity);

                return products is null || !products.Any() ?
                    NotFound("There no products found under entered requirements") : Ok(products);
            }
            catch (Exception ex)
            {
                LoggError(ex.Message.ToString(), ex.StackTrace!);
                return Problem(ex.Message);
            }
        }


        [HttpGet, Route("book/id")]
        public async Task<ActionResult<Product>> GetProductById([FromQuery]/*[StringLength(20)]*/ string id)
        {
            try
            {
                var product = (await _services.GetBookByIdAsync(id));
                return product is null || !product.IsAvailable ?
                    NotFound("There no product found under entered requirements") :
                    Ok(product);
            }
            catch (Exception ex)
            {
                LoggError(ex.Message.ToString(), ex.StackTrace!);
                return Problem(ex.Message);
            }
        }

        //return all genres in database
        [HttpGet, Route("books/genres")]
        public async Task<ActionResult<List<string>>> GetAllGenres()
        {
            try
            {
                var products = await _services.GetAllBooksAsync();
                if (products.Count != 0)
                {
                    List<string> genres = [];

                    foreach (var product in products)
                    {
                        if (product.Genres.Length != 0 && product.IsAvailable)
                        {
                            genres.AddRange(product.Genres.Where(g => !genres.Any(_ => _.Trim().Equals(g.Trim(), StringComparison.OrdinalIgnoreCase))));
                        }
                    }
                    return genres.Count != 0 ? Ok(genres) : NotFound("No record found");
                }
                else return NotFound("No record found");
            }
            catch (Exception ex)
            {
                LoggError(ex.Message, ex.StackTrace!);
                return NotFound("No records found");
            }
        }

        //returns all books in genre
        [HttpGet, Route("books/genre")]
        public async Task<ActionResult<List<Product>>> GetProductsInGenre([FromQuery] string genre, [FromQuery] PageModel model)
        {
            try
            {
                Query query = new(model.RequestedPage, model.QuantityPerPage, GetQuantityInGenre(genre).Result);

                var products = (await _services.GetAllBooksAsync()).Where(_ => 
                _.IsAvailable && 
                _.Genres.Contains(genre, StringComparer.OrdinalIgnoreCase));

                List<Product> result = OrderBy(model, [.. products]);
                return result.Count != 0 ?
                     Ok(result.Skip(query.QuantityToSkip).Take(query.RequestedQuantity)) :
                     NotFound("No record found under requsted condition");

            }
            catch (Exception ex)
            {
                LoggError(ex.Message, ex.StackTrace!);
                return Problem(ex.Message);
            }
        }
        #endregion

        #region of Filtering Methods
        //returns list of ALL books where Title,Author, Language or one of item from array of Genres EQUALS to searchCondition
        [HttpGet, Route("books/filter/condition/equals")]
        public async Task<ActionResult<List<Product>>> GetProductsEqualsCondition([FromQuery] string condition, [FromQuery] PageModel model)
        {
            try
            {
                var products = (await _services.GetBooksEqualsConditionAsync(condition, model.InAscendingOrder, model.OrderBy)).Where(_ => _.IsAvailable).ToList();

                Query query = new(model.RequestedPage, model.QuantityPerPage, products.Count);

                return products.Count != 0 ?
                    Ok(products.Skip(query.QuantityToSkip).Take(query.RequestedQuantity)) :
                    NotFound("There nor records found under requested condition");
            }
            catch (Exception ex)
            {
                LoggError(ex.Message, ex.StackTrace!);
                return Problem(ex.Message);
            }
        }

        //returns list of ALL books where Title,Author, Language or one of item from array of Genres CONTAINS to searchCondition
        [HttpGet, Route("books/filter/condition/contains")]
        public async Task<ActionResult<List<Product>>> GetProductsContainsCondition([FromQuery] string condition, [FromQuery] PageModel model)
        {
            try
            {
                var products = (await _services.GetBooksContainsConditionAsync(condition, model.InAscendingOrder, model.OrderBy)).Where(_ => _.IsAvailable).ToList();

                Query query = new(model.RequestedPage, model.QuantityPerPage, products.Count);

                return products.Count != 0 ?
                    Ok(products.Skip(query.QuantityToSkip).Take(query.RequestedQuantity)) :
                    NotFound("There nor records found under requested condition");
            }
            catch (Exception ex)
            {
                LoggError(ex.Message, ex.StackTrace!);
                return Problem(ex.Message);
            }
        }

        //Filters the list by each provided parameter in FilterProducts
        //Exception FilterProducts.IsAvailable always is TRUE
        //Devides content into pages by parameters requested or auto from PageModel
        [HttpGet, Route("books/filter/")]
        public async Task<ActionResult<List<Product>>> GetProductsFilter([FromQuery] FilterProducts filter, [FromQuery] PageModel model)
        {
            try
            {
                var products = await _services.GetBooksInOrder(model.InAscendingOrder, model.OrderBy);
                List<Product> result = ApplyFilters(products, filter);
                result = OrderBy(model, result);
                if (result.Count != 0)
                {
                    Query query = new(model.RequestedPage, model.QuantityPerPage, result.Count);
                    result = [.. result.Skip(query.QuantityToSkip).Take(query.RequestedQuantity)];
                }

                return result.Count != 0 ?
                        Ok(result) :
                        NotFound("Nor records found under requested condition");
            }
            catch (Exception ex)
            {
                LoggError(ex.Message, ex.StackTrace!);
                return Problem(ex.Message);
            }
        }
        #endregion
        #endregion

        #region of Help Methods
        private ActionResult LoggError(string errorMessage, string errorStackTrace)
        {
            _logger.LogError(message: errorMessage, args: errorStackTrace);
            return Problem(errorMessage.ToJson());
        }
        private void LoggInfo(int statusCode, string message) =>
            _logger.LogInformation(message: message + ", DateTime: {@DateTime}, StatusCode: {@statusCode}", DateTime.Now, statusCode);
        private void LogingWarning(int statusCode, string message) =>
            _logger.LogWarning(message: message + ", DateTime: {@DateTime}, StatusCode: {@statusCode}", DateTime.Now, statusCode);
        private ActionResult Warning(string message, int statusCode)
        {
            LogingWarning(statusCode, message);
            return statusCode == (int)HttpStatusCode.Unauthorized ?
                Unauthorized(message.ToJson()) :
                statusCode == (int)HttpStatusCode.NotFound ?
                NotFound(message.ToJson()) :
                statusCode == (int)HttpStatusCode.BadRequest ?
                BadRequest(message.ToJson()) :
                Problem(message.ToJson());
        }

        //Applyes FilterProducts to the List<Product>, and returns filtered list
        //Exception FilterProducts.IsAvailable always is TRUE
        private List<Product> ApplyFilters(List<Product> products, FilterProducts filter)
        {
            try
            {
                if (products.Count != 0)
                {
                    List<Product> filtered = [.. products];

                    filtered = [.. filtered.Where(product => product.IsAvailable == true)];

                    filtered = !string.IsNullOrEmpty(filter.Author) ?
                        [.. filtered.Where(product => product.Author!.Trim().Contains(filter.Author.Trim(), StringComparison.OrdinalIgnoreCase))] : filtered;

                    filtered = !string.IsNullOrEmpty(filter.Title) ?
                        [.. filtered.Where(product => product.Title!.Trim().Contains(filter.Title.Trim(), StringComparison.OrdinalIgnoreCase))] : filtered;

                    filtered = !string.IsNullOrEmpty(filter.Annotation) ?
                        [.. filtered.Where(product => product.Annotation!.Trim().Contains(filter.Annotation.Trim(), StringComparison.OrdinalIgnoreCase))] : filtered;

                    filtered = !string.IsNullOrEmpty(filter.Language) ?
                        [.. filtered.Where(product => product.Language!.Trim().Contains(filter.Language.Trim(), StringComparison.OrdinalIgnoreCase))] : filtered;

                    filtered = filter.Genres is not null ?
                        [.. filtered.Where(product => product.Genres!.Any(genre => filter.Genres!.Any(g => g.Contains(genre, StringComparison.OrdinalIgnoreCase))))] : filtered;

                    filtered = filter.MinPrice > 0 && filter.MaxPrice > 0 ?
                        [.. filtered.Where(product => product.Price >= filter.MinPrice && product.Price <= filter.MaxPrice)] :
                        filter.MinPrice > 0 ?
                        [.. filtered.Where(product => product.Price >= filter.MinPrice)] :
                        filter.MaxPrice > 0 ?
                        [.. filtered.Where(product => product.Price <= filter.MaxPrice)] :
                        filtered;

                    return filtered;
                }
                return [];
            }
            catch (Exception ex)
            {
                LoggError(ex.Message, ex.StackTrace!);
                return [];
            }
        }
        #region of count methods
        //returns quantity of all products.isAvailable in database
        [HttpGet, Route("books/count/all")]
        public async Task<int> GetQuantityAllProducts()
        {
            try
            {
                int quantity = (await _services.GetAllBooksAsync()).Where(_ => _.IsAvailable).ToList().Count;
                return quantity;
            }
            catch (Exception ex)
            {
                LoggError(ex.Message.ToString(), ex.StackTrace!);
                return 0;
            }
        }

        [HttpGet, Route("books/count/ingenre")]
        public async Task<int> GetQuantityInGenre([FromQuery] string genre)
        {
            try
            {
                int quantity = (await _services.GetAllBooksAsync()).Where(_ => 
                _.IsAvailable && 
                _.Genres.Contains(genre, StringComparer.OrdinalIgnoreCase)).Count();
                return quantity;
            }
            catch (Exception ex)
            {
                LoggError(ex.Message, ex.StackTrace!);
                return 0;
            }
        }

        [HttpGet, Route("books/filter/count/condition")]
        public async Task<int> CountPagesEqualsCondition([FromQuery] string condition, [FromQuery] PageModel model)
        {
            try
            {
                int quantity = ((await _services.GetBooksContainsConditionAsync(condition, model.InAscendingOrder, model.OrderBy)).Where(_ => _.IsAvailable).ToList()).Count;
                return quantity;
            }
            catch (Exception ex)
            {
                LoggError(ex.Message, ex.StackTrace!);
                return 0;
            }
        }
        #region of Sorting Methods
        //MAY RETURN NULL LIST
        //returns sorted list
        private List<Product> OrderBy(PageModel model, List<Product> products)
        {
            try
            {
                if (products.Count != 0)
                {
                    List<Product> result = [];
                    if (model.InAscendingOrder)
                    {
                        result = model.OrderBy.Equals("author", StringComparison.OrdinalIgnoreCase) ?
                            [.. products.OrderBy(_ => _.Author)] :
                            model.OrderBy.Equals("title", StringComparison.OrdinalIgnoreCase) ?
                            [.. products.OrderBy(_ => _.Title)] :
                            [.. products.OrderBy(_ => _.Price)];
                    }
                    else
                    {
                        result = model.OrderBy.Equals("author", StringComparison.OrdinalIgnoreCase) ?
                            [.. products.OrderByDescending(_ => _.Author)] :
                            model.OrderBy.Equals("title", StringComparison.OrdinalIgnoreCase) ?
                            [.. products.OrderByDescending(_ => _.Title)] :
                            [.. products.OrderByDescending(_ => _.Price)];
                    }

                    return result;
                }
                return [];
            }
            catch (Exception ex)
            {
                LoggError(ex.Message, ex.StackTrace!);
                return [];
            }
        }

        //Returns total number of pages for method  GetProductsFilter()
        [HttpGet, Route("books/filter/count")]
        public async Task<ActionResult<Query>> CountPagesFilter([FromQuery] FilterProducts filter, [FromQuery] PageModel model)
        {
            try
            {
                var products = (await _services.GetBooksInOrder(model.InAscendingOrder, model.OrderBy)).Where(_ => _.IsAvailable).ToList();
                if (products.Count != 0)
                {
                    Query query = new(model.RequestedPage, model.QuantityPerPage, ApplyFilters(products, filter).Count);
                    return Ok(query);
                }
                return BadRequest("Unable to process your request");
            }
            catch (Exception ex)
            {
                LoggError(ex.Message, ex.StackTrace!);
                return Problem(ex.Message);
            }
        }
        #endregion
        #endregion
        #endregion
    }

    [ApiController]
    [ApiVersion("3")]
    [Produces("application/json")]
    [Consumes("application/json")]
    [EnableCors(PolicyName = "MyPolicyForGuest")]
    public class StockV3Controller(StockDBServices services, ILogger<StockV3Controller> logger) : ControllerBase
    {
        private readonly StockDBServices _services = services;
        private readonly ILogger<StockV3Controller> _logger = logger;

        //return all genres in database
        [HttpGet, Route("books/genres")]
        public async Task<ActionResult<List<string>>> GetAllGenres()
        {
            try
            {
                var products = await _services.GetAllBooksAsync();
                if (products.Count != 0)
                {
                    List<string> genres = [];

                    foreach (var product in products)
                    {
                        if (product.Genres.Length != 0 && product.IsAvailable)
                        {
                            genres.AddRange(product.Genres.Where(g => !genres.Any(_ => _.Trim().Equals(g.Trim(), StringComparison.OrdinalIgnoreCase))));
                        }
                    }
                    return genres.Count != 0 ? Ok(genres) : NotFound("No record found");
                }
                else return NotFound("No record found");
            }
            catch (Exception ex)
            {
                LoggError(ex.Message, ex.StackTrace!);
                return NotFound("No records found");
            }
        }

        //Filters the list by each provided parameter in FilterProducts
        //Exception FilterProducts.IsAvailable always is TRUE
        //Devides content into pages by parameters requested or auto from PageModel
        [HttpGet, Route("books/filter/")]
        public async Task<ActionResult<List<Product>>> GetProductsFilter([FromQuery] FilterProducts filter, [FromQuery] PageModel model)
        {
            try
            {
                var products = await _services.GetBooksInOrder(model.InAscendingOrder, model.OrderBy);
                List<Product> result = ApplyFilters(products, filter);
                result = OrderBy(model, result);
                if (result.Count != 0)
                {
                    Query query = new(model.RequestedPage, model.QuantityPerPage, result.Count);
                    result = [.. result.Skip(query.QuantityToSkip).Take(query.RequestedQuantity)];
                }

                return result.Count != 0 ?
                        Ok(result) :
                        NotFound("Nor records found under requested condition");
            }
            catch (Exception ex)
            {
                LoggError(ex.Message, ex.StackTrace!);
                return Problem(ex.Message);
            }
        }

        [HttpGet, Route("book/id")]
        public async Task<ActionResult<Product>> GetProductById([FromQuery]/*[StringLength(20)]*/ string id)
        {
            try
            {
                var product = (await _services.GetBookByIdAsync(id));
                return product is null || !product.IsAvailable ?
                    NotFound("There no product found under entered requirements") :
                    Ok(product);
            }
            catch (Exception ex)
            {
                LoggError(ex.Message.ToString(), ex.StackTrace!);
                return Problem(ex.Message);
            }
        }

        #region Of Help Methods
        private ActionResult LoggError(string errorMessage, string errorStackTrace)
        {
            _logger.LogError(message: errorMessage, args: errorStackTrace);
            return Problem(errorMessage.ToJson());
        }
        private void LoggInfo(int statusCode, string message) =>
            _logger.LogInformation(message: message + ", DateTime: {@DateTime}, StatusCode: {@statusCode}", DateTime.Now, statusCode);
        private void LogingWarning(int statusCode, string message) =>
            _logger.LogWarning(message: message + ", DateTime: {@DateTime}, StatusCode: {@statusCode}", DateTime.Now, statusCode);
        private ActionResult Warning(string message, int statusCode)
        {
            LogingWarning(statusCode, message);
            return statusCode == (int)HttpStatusCode.Unauthorized ?
                Unauthorized(message.ToJson()) :
                statusCode == (int)HttpStatusCode.NotFound ?
                NotFound(message.ToJson()) :
                statusCode == (int)HttpStatusCode.BadRequest ?
                BadRequest(message.ToJson()) :
                Problem(message.ToJson());
        }

        //Applyes FilterProducts to the List<Product>, and returns filtered list
        //Exception FilterProducts.IsAvailable always is TRUE
        private List<Product> ApplyFilters(List<Product> products, FilterProducts filter)
        {
            try
            {
                if (products.Count != 0)
                {
                    List<Product> filtered = [.. products];

                    filtered = [.. filtered.Where(product => product.IsAvailable == true)];

                    filtered = !string.IsNullOrEmpty(filter.Author) ?
                        [.. filtered.Where(product => product.Author!.Trim().Contains(filter.Author.Trim(), StringComparison.OrdinalIgnoreCase))] : filtered;

                    filtered = !string.IsNullOrEmpty(filter.Title) ?
                        [.. filtered.Where(product => product.Title!.Trim().Contains(filter.Title.Trim(), StringComparison.OrdinalIgnoreCase))] : filtered;

                    filtered = !string.IsNullOrEmpty(filter.Annotation) ?
                        [.. filtered.Where(product => product.Annotation!.Trim().Contains(filter.Annotation.Trim(), StringComparison.OrdinalIgnoreCase))] : filtered;

                    filtered = !string.IsNullOrEmpty(filter.Language) ?
                        [.. filtered.Where(product => product.Language!.Trim().Contains(filter.Language.Trim(), StringComparison.OrdinalIgnoreCase))] : filtered;

                    filtered = filter.Genres is not null ?
                        [.. filtered.Where(product => product.Genres!.Any(genre => filter.Genres!.Any(g => g.Contains(genre, StringComparison.OrdinalIgnoreCase))))] : filtered;

                    filtered = filter.MinPrice > 0 && filter.MaxPrice > 0 ?
                        [.. filtered.Where(product => product.Price >= filter.MinPrice && product.Price <= filter.MaxPrice)] :
                        filter.MinPrice > 0 ?
                        [.. filtered.Where(product => product.Price >= filter.MinPrice)] :
                        filter.MaxPrice > 0 ?
                        [.. filtered.Where(product => product.Price <= filter.MaxPrice)] :
                        filtered;

                    return filtered;
                }
                return [];
            }
            catch (Exception ex)
            {
                LoggError(ex.Message, ex.StackTrace!);
                return [];
            }
        }
        //MAY RETURN NULL LIST
        //returns sorted list
        private List<Product> OrderBy(PageModel model, List<Product> products)
        {
            try
            {
                if (products.Count != 0)
                {
                    List<Product> result = [];
                    if (model.InAscendingOrder)
                    {
                        result = model.OrderBy.Equals("author", StringComparison.OrdinalIgnoreCase) ?
                            [.. products.OrderBy(_ => _.Author)] :
                            model.OrderBy.Equals("title", StringComparison.OrdinalIgnoreCase) ?
                            [.. products.OrderBy(_ => _.Title)] :
                            [.. products.OrderBy(_ => _.Price)];
                    }
                    else
                    {
                        result = model.OrderBy.Equals("author", StringComparison.OrdinalIgnoreCase) ?
                            [.. products.OrderByDescending(_ => _.Author)] :
                            model.OrderBy.Equals("title", StringComparison.OrdinalIgnoreCase) ?
                            [.. products.OrderByDescending(_ => _.Title)] :
                            [.. products.OrderByDescending(_ => _.Price)];
                    }

                    return result;
                }
                return [];
            }
            catch (Exception ex)
            {
                LoggError(ex.Message, ex.StackTrace!);
                return [];
            }
        }

        #endregion
    }


}
