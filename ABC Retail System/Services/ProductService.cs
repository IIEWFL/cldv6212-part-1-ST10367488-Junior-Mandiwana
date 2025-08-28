using ABC_Retail_System.Models;
using ABC_Retail_System.Services.Storage;
using Azure;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ABC_Retail_System.Services
{
    public class ProductService
    {
        private readonly TableStorageService _tableStorage;
        private readonly QueueStorageService _queueStorage;
        private const string PartitionKey = "PRODUCT";
        private const string QueueName = "abcretail-queue";
        
        public ProductService(TableStorageService tableStorage, QueueStorageService queueStorage)
        {
            _tableStorage = tableStorage ?? throw new ArgumentNullException(nameof(tableStorage));
            _queueStorage = queueStorage ?? throw new ArgumentNullException(nameof(queueStorage));
        }

        public async Task<List<Product>> GetAllAsync()
        {
            var products = await _tableStorage.GetEntitiesAsync<Product>(PartitionKey);
            return products ?? new List<Product>();
        }
        
        public async Task<Product> GetAsync(string id)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Product ID cannot be null or empty", nameof(id));
                
            return await _tableStorage.GetEntityAsync<Product>(PartitionKey, id);
        }
            
        public async Task AddAsync(Product product)
        {
            if (product == null)
                throw new ArgumentNullException(nameof(product));

            // Ensure required ITableEntity properties are set
            product.PartitionKey = PartitionKey;
            product.RowKey = string.IsNullOrEmpty(product.RowKey) ? Guid.NewGuid().ToString() : product.RowKey;
            product.Timestamp = DateTimeOffset.UtcNow;
            product.ETag = ETag.All;

            await _tableStorage.AddEntityAsync(product);
            
            // Log the action
            try
            {
                await _queueStorage.SendLogEntryAsync(new { 
                    Action = "New Product Added", 
                    Entity = "Product", 
                    ProductName = product.ProductName,
                    ProductId = product.RowKey,
                    Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"), 
                    Details = $"Added product {product.ProductName} (ID: {product.RowKey})" 
                });
            }
            catch (Exception ex)
            {
                // Log the error but don't fail the operation
                Console.WriteLine($"Error sending log entry: {ex.Message}");
            }
        }
        
        public async Task UpdateAsync(Product product)
        {
            if (product == null)
                throw new ArgumentNullException(nameof(product));
                
            if (string.IsNullOrEmpty(product.PartitionKey) || string.IsNullOrEmpty(product.RowKey))
                throw new ArgumentException("Product must have valid PartitionKey and RowKey");

            // Get the original product to compare changes
            var originalProduct = await GetAsync(product.RowKey);
            
            // Ensure Timestamp is updated
            product.Timestamp = DateTimeOffset.UtcNow;
            
            await _tableStorage.UpdateEntityAsync(product, product.ETag);
            
            // Log the update action
            try
            {
                var changes = new List<string>();
                if (originalProduct.ProductName != product.ProductName)
                    changes.Add($"Name: {originalProduct.ProductName} → {product.ProductName}");
                if (originalProduct.Price != product.Price)
                    changes.Add($"Price: {originalProduct.Price} → {product.Price}");
                if (originalProduct.StockQuantity != product.StockQuantity)
                    changes.Add($"Stock: {originalProduct.StockQuantity} → {product.StockQuantity}");
                
                await _queueStorage.SendLogEntryAsync(new { 
                    Action = "Product Updated", 
                    Entity = "Product", 
                    ProductId = product.RowKey,
                    Changes = changes,
                    Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    Details = $"Updated product {product.ProductName} (ID: {product.RowKey}): {string.Join(", ", changes)}" 
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error logging product update: {ex.Message}");
            }
        }
        
        public async Task DeleteAsync(string id)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Product ID cannot be null or empty", nameof(id));
                
            var product = await GetAsync(id);
            if (product != null)
            {
                await _tableStorage.DeleteEntityAsync<Product>(PartitionKey, id);
                await _queueStorage.SendLogEntryAsync(new { 
                    Action = "Product Deleted", 
                    Entity = "Product", 
                    ProductName = product.ProductName,
                    ProductId = product.RowKey,
                    Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"), 
                    Details = $"Deleted product {product.ProductName} (ID: {product.RowKey})" 
                });
            }
        }

        public async Task UpdateStockAsync(string id, int quantityChange)
        {
            var product = await GetAsync(id);
            if (product != null)
            {
                product.StockQuantity = (product.StockQuantity ?? 0) + quantityChange;
                await UpdateAsync(product);
            }
        }

        internal async Task<IEnumerable> GetProductsAsync()
        {
            throw new NotImplementedException();
        }
    }
}
