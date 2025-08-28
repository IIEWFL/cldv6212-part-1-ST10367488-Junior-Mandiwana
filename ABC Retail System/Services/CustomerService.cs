using ABC_Retail_System.Models;
using ABC_Retail_System.Services.Storage;
using Azure;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ABC_Retail_System.Services
{
    public class CustomerService
    {
        private readonly TableStorageService _tableStorage;
        private readonly QueueStorageService _queueStorage;
        private const string PartitionKey = "CUSTOMER";
        private const string QueueName = "customer-logs";
        
        public CustomerService(TableStorageService tableStorage, QueueStorageService queueStorage)
        {
            _tableStorage = tableStorage ?? throw new ArgumentNullException(nameof(tableStorage));
            _queueStorage = queueStorage ?? throw new ArgumentNullException(nameof(queueStorage));
        }

        public async Task<List<Customer>> GetAllAsync()
        {
            var customers = await _tableStorage.GetEntitiesAsync<Customer>(PartitionKey);
            return customers ?? new List<Customer>();
        }
        
        public async Task<Customer> GetAsync(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
                
            try
            {
                var customer = await _tableStorage.GetEntityAsync<Customer>(PartitionKey, id);
                if (customer != null)
                {
                    // Ensure ETag is properly set
                    if (customer.ETag == default)
                    {
                        customer.ETag = ETag.All;
                    }
                }
                return customer;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving customer with ID {id}: {ex.Message}");
                throw;
            }
        }
        
        // Overload to support rowKey parameter name for backward compatibility
        public async Task<Customer> GetByRowKeyAsync(string rowKey)
        {
            return await GetAsync(rowKey);
        }
            
        public async Task AddAsync(Customer customer)
        {
            if (customer == null)
                throw new ArgumentNullException(nameof(customer));

            // Ensure required ITableEntity properties are set
            customer.PartitionKey = PartitionKey;
            customer.RowKey = string.IsNullOrEmpty(customer.RowKey) ? Guid.NewGuid().ToString() : customer.RowKey;
            customer.Timestamp = DateTimeOffset.UtcNow;
            customer.ETag = ETag.All;

            await _tableStorage.AddEntityAsync(customer);
            await _queueStorage.SendLogEntryAsync(new { 
                Action = "New Customer Added", 
                Entity = "Customer", 
                CustomerName = $"{customer.FirstName} {customer.LastName}",
                CustomerId = customer.RowKey,
                Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"), 
                Details = $"Added customer {customer.FirstName} {customer.LastName} (ID: {customer.RowKey})" 
            });
        }
        
        public async Task UpdateAsync(Customer customer)
        {
            if (customer == null)
                throw new ArgumentNullException(nameof(customer));
                
            if (string.IsNullOrEmpty(customer.PartitionKey) || string.IsNullOrEmpty(customer.RowKey))
                throw new ArgumentException("Customer must have valid PartitionKey and RowKey");

            try
            {
                // Ensure required fields are set
                customer.PartitionKey = PartitionKey;
                customer.Timestamp = DateTimeOffset.UtcNow;
                if (customer.ETag == default)
                {
                    customer.ETag = ETag.All;
                }
                
                await _tableStorage.UpdateEntityAsync(customer);
                
                await _queueStorage.SendLogEntryAsync(new { 
                    Action = "Customer Updated", 
                    Entity = "Customer", 
                    CustomerName = $"{customer.FirstName} {customer.LastName}",
                    CustomerId = customer.RowKey,
                    Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"), 
                    Details = $"Updated customer {customer.FirstName} {customer.LastName} (ID: {customer.RowKey})" 
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating customer {customer.RowKey}: {ex}");
                throw;
            }
        }
        
        public async Task<bool> DeleteAsync(string id)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Customer ID cannot be null or empty", nameof(id));
                
            try
            {
                var customer = await GetAsync(id);
                if (customer == null)
                    return false;
                    
                await _tableStorage.DeleteEntityAsync<Customer>(PartitionKey, id);
                
                await _queueStorage.SendLogEntryAsync(new { 
                    Action = "Customer Deleted", 
                    Entity = "Customer", 
                    CustomerName = $"{customer.FirstName} {customer.LastName}",
                    CustomerId = customer.RowKey,
                    Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"), 
                    Details = $"Deleted customer {customer.FirstName} {customer.LastName} (ID: {customer.RowKey})" 
                });
                
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting customer {id}: {ex}");
                throw;
            }
        }

        internal async Task GetCustomerAsync(string? customerRowKey)
        {
            throw new NotImplementedException();
        }

        internal async Task<IEnumerable> GetCustomersAsync()
        {
            throw new NotImplementedException();
        }
    }
}
