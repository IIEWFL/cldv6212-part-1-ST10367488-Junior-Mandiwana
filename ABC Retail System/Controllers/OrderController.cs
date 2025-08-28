using ABC_Retail_System.Models;
using ABC_Retail_System.Services;
using Azure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ABC_Retail_System.Controllers
{
    public class OrderController : Controller
    {
        private readonly OrderService _orderService;
        private readonly CustomerService _customerService;
        private readonly ProductService _productService;
        private readonly QueueLoggerService _queueLogger;
        private const string PartitionKey = "ORDER";

        public OrderController(
            OrderService orderService, 
            CustomerService customerService,
            ProductService productService,
            QueueLoggerService queueLogger)
        {
            _orderService = orderService ?? throw new ArgumentNullException(nameof(orderService));
            _customerService = customerService ?? throw new ArgumentNullException(nameof(customerService));
            _productService = productService ?? throw new ArgumentNullException(nameof(productService));
            _queueLogger = queueLogger ?? throw new ArgumentNullException(nameof(queueLogger));
        }

        // GET: Order/Index
        public async Task<IActionResult> Index()
        {
            try
            {
                var orders = await _orderService.GetOrdersAsync();
                return View(orders);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"Error retrieving orders: {ex.Message}");
                return View(new List<Order>());
            }
        }

        // GET: Order/Create
        public async Task<IActionResult> Create()
        {
            try
            {
                await PopulateViewBags();
                return View(new Order { Status = "Pending" });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"Error loading create form: {ex.Message}");
                return View(new Order());
            }
        }

        // POST: Order/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Order order)
        {
            if (!ModelState.IsValid)
            {
                await PopulateViewBags();
                return View(order);
            }

            try
            {
                await _orderService.CreateOrderAsync(order);
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"Error creating order: {ex.Message}");
                await PopulateViewBags();
                return View(order);
            }
        }

        // GET: Order/Details/5
        [HttpGet]
        [Route("Order/Details/{id}")]
        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return BadRequest("Order ID is required.");
            }

            try
            {
                var order = await _orderService.GetOrderAsync(id);
                if (order == null) return NotFound();

                // Load customer and product details
                var customer = await _customerService.GetByRowKeyAsync(order.CustomerRowKey);
                var product = await _productService.GetAsync(order.ProductRowKey);

                if (customer != null)
                {
                    order.CustomerName = $"{customer.FirstName} {customer.LastName}";
                }

                if (product != null)
                {
                    order.ProductName = product.ProductName;
                    order.ProductPrice = product.Price;
                }

                return View(order);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"Error retrieving order: {ex.Message}");
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: Order/Edit/5
        [HttpGet]
        [Route("Order/Edit/{id}")]
        public async Task<IActionResult> Edit(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return BadRequest("Order ID is required.");
            }

            try
            {
                var order = await _orderService.GetOrderAsync(id);
                if (order == null) return NotFound();

                await PopulateViewBags();
                return View(order);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"Error loading order for editing: {ex.Message}");
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Order/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Order order)
        {
            if (order == null)
            {
                return BadRequest("Order cannot be null");
            }

            if (!ModelState.IsValid)
            {
                await PopulateViewBags();
                return View(order);
            }

            try
            {
                // Get the existing order to preserve values not in the form
                var existingOrder = await _orderService.GetOrderAsync(order.RowKey);
                if (existingOrder == null)
                {
                    return NotFound("Order not found");
                }

                // Update the existing order with new values
                existingOrder.CustomerRowKey = order.CustomerRowKey;
                existingOrder.ProductRowKey = order.ProductRowKey;
                existingOrder.Quantity = order.Quantity;
                existingOrder.Status = order.Status;
                existingOrder.Timestamp = DateTimeOffset.UtcNow;

                await _orderService.UpdateOrderAsync(existingOrder);
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"Error updating order: {ex.Message}");
                await PopulateViewBags();
                return View(order);
            }
        }

        // GET: Order/Delete/5
        public async Task<IActionResult> Delete(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return BadRequest("Order ID is required.");
            }
            var order = await _orderService.GetOrderAsync(id);
            if (order == null) return NotFound();
            return View(order);
        }

        private async Task<Order> GetOrderWithRelatedDataAsync(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            
            var order = await _orderService.GetOrderAsync(id);
            if (order == null) return null;
            
            // Load related data
            var customer = await _customerService.GetByRowKeyAsync(order.CustomerRowKey);
            var product = await _productService.GetAsync(order.ProductRowKey);
            
            if (customer != null) order.CustomerName = $"{customer.FirstName} {customer.LastName}";
            if (product != null) order.ProductName = product.ProductName;
            
            return order;
        }

        // POST: Order/Delete
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string id, string eTag = null)
        {
            if (string.IsNullOrEmpty(id))
            {
                ModelState.AddModelError(string.Empty, "Order ID cannot be empty");
                return BadRequest(ModelState);
            }

            try
            {
                await _orderService.DeleteOrderAsync(id, eTag);
                TempData["SuccessMessage"] = "Order deleted successfully!";
                return RedirectToAction(nameof(Index));
            }
            catch (KeyNotFoundException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return NotFound();
            }
            catch (InvalidOperationException ex) when (ex.InnerException is RequestFailedException rfe && rfe.Status == 412)
            {
                // Concurrency conflict
                ModelState.AddModelError(string.Empty, ex.Message);
                var order = await GetOrderWithRelatedDataAsync(id);
                return order != null ? View("Delete", order) : RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"Error deleting order: {ex.Message}");
                var order = await GetOrderWithRelatedDataAsync(id);
                return order != null ? View("Delete", order) : RedirectToAction(nameof(Index));
            }
        }

        private async Task PopulateViewBags()
        {
            try
            {
                var customers = await _customerService.GetAllAsync();
                ViewBag.CustomerOptions = new SelectList(customers, "RowKey", "FullName");

                var products = await _productService.GetAllAsync();
                ViewBag.ProductOptions = new SelectList(products, "RowKey", "ProductName");
            }
            catch (Exception ex)
            {
                // Log the error if needed
                Console.WriteLine($"Error populating view bags: {ex.Message}");
            }
        }
    }
}
