using ABC_Retail_System.Models;
using Azure;
using Azure.Data.Tables;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ABC_Retail_System.Services.Storage
{
    public class TableStorageService
    {
        private readonly TableClient _tableClient;

        public TableStorageService(string storageConnectionString, string tableName)
        {
            try
            {
                Console.WriteLine($"[DEBUG] Initializing TableStorageService for table: {tableName}");
                var serviceClient = new TableServiceClient(storageConnectionString);
                _tableClient = serviceClient.GetTableClient(tableName);
                
                // Create the table if it doesn't exist
                var response = _tableClient.CreateIfNotExists();
                Console.WriteLine($"[DEBUG] Table {tableName} exists or was created: {response != null}");
                
                // Log the table URI for verification
                Console.WriteLine($"[DEBUG] Table URI: {_tableClient.Uri}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Error initializing TableStorageService: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"[ERROR] Inner exception: {ex.InnerException.Message}");
                }
                throw;
            }
        }

        // Generic methods for any entity type
        public async Task<List<T>> GetEntitiesAsync<T>(string partitionKey = null) where T : class, ITableEntity, new()
        {
            var entities = new List<T>();
            
            try
            {
                var query = partitionKey != null 
                    ? _tableClient.QueryAsync<T>(e => e.PartitionKey == partitionKey)
                    : _tableClient.QueryAsync<T>();

                await foreach (var entity in query)
                {
                    if (entity != null)
                    {
                        entities.Add(entity);
                    }
                }
            }
            catch (Exception ex)
            {
                // Log the error if needed
                Console.WriteLine($"Error retrieving entities: {ex.Message}");
            }

            return entities;
        }

        public async Task<T> GetEntityAsync<T>(string partitionKey, string rowKey) where T : class, ITableEntity
        {
            try
            {
                var response = await _tableClient.GetEntityAsync<T>(partitionKey, rowKey);
                return response.Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task AddEntityAsync<T>(T entity) where T : ITableEntity
        {
            try
            {
                Console.WriteLine($"[DEBUG] Adding entity to table {_tableClient.Name}:");
                Console.WriteLine($"[DEBUG] Type: {typeof(T).Name}");
                
                if (string.IsNullOrEmpty(entity.PartitionKey))
                    entity.PartitionKey = typeof(T).Name.ToUpper();

                if (string.IsNullOrEmpty(entity.RowKey))
                    entity.RowKey = Guid.NewGuid().ToString();

                Console.WriteLine($"[DEBUG] PartitionKey: {entity.PartitionKey}");
                Console.WriteLine($"[DEBUG] RowKey: {entity.RowKey}");
                
                var response = await _tableClient.AddEntityAsync(entity);
                Console.WriteLine($"[DEBUG] Entity added successfully. Status: {response.Status}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Error adding entity: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"[ERROR] Inner exception: {ex.InnerException.Message}");
                }
                throw;
            }
        }

        public async Task UpdateEntityAsync<T>(T entity, ETag? etag = null) where T : ITableEntity
        {
            await _tableClient.UpdateEntityAsync(entity, etag ?? entity.ETag, TableUpdateMode.Replace);
        }

        public async Task UpdateEntityAsync<T>(T entity) where T : class, ITableEntity
        {
            await _tableClient.UpdateEntityAsync(entity, ETag.All);
        }

        public async Task DeleteEntityAsync<T>(string partitionKey, string rowKey) where T : class, ITableEntity
        {
            await _tableClient.DeleteEntityAsync(partitionKey, rowKey);
        }

        public async Task<List<T>> QueryEntitiesAsync<T>(string filter) where T : class, ITableEntity
        {
            var entities = new List<T>();
            var query = _tableClient.QueryAsync<T>(filter);

            await foreach (var entity in query)
            {
                entities.Add(entity);
            }

            return entities;
        }

        public TableClient GetTableClient(string tableName)
        {
            return _tableClient;
        }

        internal string GetTableName()
        {
            return _tableClient.Name;
        }
    }
}