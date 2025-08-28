using ABC_Retail_System.Models;
using ABC_Retail_System.Services;
using ABC_Retail_System.Services.Storage;
using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;
using System.Threading.Tasks;

namespace ABC_Retail_System.Controllers
{
    public class ProductController : Controller
    {
        private readonly ProductService _productService;
        private readonly BlobStorageService _blobService;
        private readonly QueueLoggerService _queueLogger;
        private readonly ILogger<ProductController> _logger;

        public ProductController(ProductService productService, BlobStorageService blobService, QueueLoggerService queueLogger, ILogger<ProductController> logger)
        {
            _productService = productService ?? throw new ArgumentNullException(nameof(productService));
            _blobService = blobService ?? throw new ArgumentNullException(nameof(blobService));
            _queueLogger = queueLogger ?? throw new ArgumentNullException(nameof(queueLogger));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // GET: Product/Index (Manage Products)
        public async Task<IActionResult> Index()
        {
            var products = await _productService.GetAllAsync();
            return View(products);
        }

        // GET: Product/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Product/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Product product, IFormFile photo)
        {
            if (!ModelState.IsValid)
            {
                return View(product);
            }

            try
            {
                if (photo != null && photo.Length > 0)
                {
                    using var stream = photo.OpenReadStream();
                    product.ImageUrl = await _blobService.UploadPhotoAsync(Guid.NewGuid().ToString(), stream);
                }

                await _productService.AddAsync(product);
                // Log the create operation
                await _queueLogger.LogOperationAsync("Create", "Product", product.RowKey, $"Created product: {product.ProductName} (ID: {product.RowKey})");
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"Error creating product: {ex.Message}");
                return View(product);
            }
        }

        // GET: Product/Details/5
        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return BadRequest("Product ID is required.");
            }
            var product = await _productService.GetAsync(id);
            if (product == null) return NotFound();
            return View(product);
        }

        // GET: Product/Edit/5
        [HttpGet]
        [Route("Product/Edit/{id}")]
        public async Task<IActionResult> Edit(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return BadRequest("Product ID is required.");
            }
            var product = await _productService.GetAsync(id);
            if (product == null) return NotFound();
            return View(product);
        }

        // POST: Product/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Product product, IFormFile photo)
        {
            if (product == null)
            {
                return BadRequest("Product cannot be null");
            }

            if (!ModelState.IsValid)
            {
                return View(product);
            }

            try
            {
                // Get the existing product to preserve values not in the form
                var existingProduct = await _productService.GetAsync(product.RowKey);
                if (existingProduct == null)
                {
                    return NotFound("Product not found");
                }

                // Update the existing product with new values
                existingProduct.ProductName = product.ProductName;
                existingProduct.Price = product.Price;
                existingProduct.StockQuantity = product.StockQuantity;
                existingProduct.Brand = product.Brand;
                existingProduct.Colour = product.Colour;
                existingProduct.Size = product.Size;

                // Handle file upload if a new photo was provided
                if (photo != null && photo.Length > 0)
                {
                    using var stream = photo.OpenReadStream();
                    existingProduct.ImageUrl = await _blobService.UploadPhotoAsync(Guid.NewGuid().ToString(), stream);
                }
                // If no new photo but existing one, keep the existing image
                else if (string.IsNullOrEmpty(existingProduct.ImageUrl) && !string.IsNullOrEmpty(product.ImageUrl))
                {
                    existingProduct.ImageUrl = product.ImageUrl;
                }

                // Ensure required fields are set
                existingProduct.PartitionKey = "PRODUCT";
                existingProduct.Timestamp = DateTimeOffset.UtcNow;

                await _productService.UpdateAsync(existingProduct);
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"Error updating product: {ex.Message}");
                return View(product);
            }
        }

        // GET: Product/Delete/5
        public async Task<IActionResult> Delete(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return BadRequest("Product ID is required.");
            }
            var product = await _productService.GetAsync(id);
            if (product == null) return NotFound();
            return View(product);
        }

        // POST: Product/Delete
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string id)
        {
            try
            {
                await _productService.DeleteAsync(id);
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"Error deleting product: {ex.Message}");
                var product = await _productService.GetAsync(id);
                if (product == null) return RedirectToAction(nameof(Index));
                return View("Delete", product);
            }
        }
    }
}