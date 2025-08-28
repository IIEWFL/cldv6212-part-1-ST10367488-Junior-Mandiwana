using ABC_Retail_System.Models;
using ABC_Retail_System.Services.Storage;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ABC_Retail_System.Services
{
    public class OrderService
    {
        private const string TableName = "orders";
        private const string PartitionKey = "Order";
        private readonly TableStorageService _tableStorage;
        private readonly CustomerService _customerService;
        private readonly ProductService _productService;
        private readonly QueueLoggerService _queueLogger;

        public OrderService(
            TableStorageService tableStorage,
            CustomerService customerService,
            ProductService productService,
            QueueLoggerService queueLogger)
        {
            _tableStorage = tableStorage ?? throw new ArgumentNullException(nameof(tableStorage));
            _customerService = customerService ?? throw new ArgumentNullException(nameof(customerService));
            _productService = productService ?? throw new ArgumentNullException(nameof(productService));
            _queueLogger = queueLogger ?? throw new ArgumentNullException(nameof(queueLogger));
            
            // Ensure the table exists
            _tableStorage.GetTableClient(TableName);
        }

        public async Task<IEnumerable<Order>> GetOrdersAsync()
        {
            var orders = await _tableStorage.GetEntitiesAsync<Order>(PartitionKey);
            
            // Populate navigation properties
            foreach (var order in orders)
            {
                var customer = await _customerService.GetAsync(order.CustomerRowKey);
                var product = await _productService.GetAsync(order.ProductRowKey);
                
                if (customer != null) order.CustomerName = $"{customer.FirstName} {customer.LastName}";
                if (product != null)
                {
                    order.ProductName = product.ProductName;
                    order.ProductPrice = product.Price;
                }
            }
            
            return orders ?? new List<Order>();
        }

        public async Task<Order> GetOrderAsync(string rowKey)
        {
            return await _tableStorage.GetEntityAsync<Order>(PartitionKey, rowKey);
        }

        public async Task CreateOrderAsync(Order order)
        {
            order.PartitionKey = PartitionKey;
            order.RowKey = string.IsNullOrEmpty(order.RowKey) ? Guid.NewGuid().ToString() : order.RowKey;
            order.Timestamp = DateTimeOffset.UtcNow;
            order.ETag = ETag.All;
            await _tableStorage.AddEntityAsync(order);
        }

        public async Task UpdateOrderAsync(Order order)
        {
            order.PartitionKey = PartitionKey;
            order.Timestamp = DateTimeOffset.UtcNow;
            await _tableStorage.UpdateEntityAsync(order);
        }

        public async Task DeleteOrderAsync(string rowKey, string eTag = null)
        {
            if (string.IsNullOrEmpty(rowKey))
                throw new ArgumentException("RowKey cannot be null or empty", nameof(rowKey));

            try
            {
                var order = await GetOrderAsync(rowKey);
                if (order == null)
                    throw new KeyNotFoundException($"Order with ID {rowKey} not found");

                // If eTag is provided, use it for optimistic concurrency
                if (!string.IsNullOrEmpty(eTag))
                {
                    order.ETag = new ETag(eTag);
                }

                // Delete the order
                await _tableStorage.DeleteEntityAsync<Order>(PartitionKey, rowKey);
                
                // Log the deletion
                try
                {
                    var customer = await _customerService.GetAsync(order.CustomerRowKey);
                    var product = await _productService.GetAsync(order.ProductRowKey);
                    
                    // Log to queue
                    await _queueLogger.LogOperationAsync("Delete", "Order", rowKey, 
                        $"Deleted order {rowKey} for customer {customer?.FirstName} {customer?.LastName}");
                }
                catch (Exception ex)
                {
                    // Log the error but don't fail the operation
                    Console.WriteLine($"Error logging order deletion: {ex.Message}");
                }
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                // Concurrency conflict - the order was modified by another process
                throw new InvalidOperationException("The order was modified by another process. Please refresh and try again.", ex);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting order {rowKey}: {ex}");
                throw;
            }
        }
    }
}
