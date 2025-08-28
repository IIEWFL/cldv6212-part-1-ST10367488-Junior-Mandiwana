using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using System.Text;

namespace ABC_Retail_System.Services.Storage
{
    public class QueueStorageService
    {
        private readonly QueueClient _queueClient;

        public QueueStorageService(string storageConnectionString, string queueName)
        {
            try
            {
                Console.WriteLine($"[QueueStorageService] Initializing with queue: {queueName}");
                var serviceClient = new QueueServiceClient(storageConnectionString);
                _queueClient = serviceClient.GetQueueClient(queueName);
                
                // Check if queue exists and is accessible
                var exists = _queueClient.Exists();
                Console.WriteLine($"[QueueStorageService] Queue '{queueName}' exists: {exists.Value}");
                
                if (!exists.Value)
                {
                    Console.WriteLine($"[QueueStorageService] Creating queue: {queueName}");
                    _queueClient.CreateIfNotExists();
                    Console.WriteLine($"[QueueStorageService] Queue '{queueName}' created successfully");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[QueueStorageService] Error initializing queue: {ex}");
                throw;
            }
        }
        
        public async Task<int> GetMessageCountAsync()
        {
            try
            {
                var properties = await _queueClient.GetPropertiesAsync();
                int count = properties.Value.ApproximateMessagesCount;
                Console.WriteLine($"[QueueStorageService] Current message count: {count}");
                return count;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[QueueStorageService] Error getting message count: {ex}");
                return -1;
            }
        }

        public async Task SendMessagesAsync(object message)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(message);
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            await _queueClient.SendMessageAsync(base64);
        }

        /// <summary>
        /// Sends a log entry to the queue.
        /// This is a convenience method that wraps SendMessagesAsync for backward compatibility.
        /// </summary>
        /// <param name="logEntry">The log entry object to send</param>
        public async Task SendLogEntryAsync(object logEntry)
        {
            try
            {
                Console.WriteLine($"[SendLogEntryAsync] Preparing to send log entry: {logEntry}");
                
                // Ensure the queue exists
                await _queueClient.CreateIfNotExistsAsync();
                
                // Send the message
                await SendMessagesAsync(logEntry);
                
                Console.WriteLine($"[SendLogEntryAsync] Successfully sent log entry to queue");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SendLogEntryAsync] Error sending log entry: {ex}");
                throw;
            }
        }

        // Peek (non-destructive) so you can show messages in the app
        public async Task<List<string>> PeekMessagesAsync(int maxMessages = 16)
        {
            Console.WriteLine($"[PeekMessagesAsync] Starting to peek up to {maxMessages} messages");
            var results = new List<string>();
            
            try 
            {
                // First, ensure the queue exists
                Console.WriteLine("[PeekMessagesAsync] Ensuring queue exists...");
                await _queueClient.CreateIfNotExistsAsync();
                
                // Get queue properties to verify access
                var properties = await _queueClient.GetPropertiesAsync();
                Console.WriteLine($"[PeekMessagesAsync] Queue properties - ApproxMessagesCount: {properties.Value?.ApproximateMessagesCount}");
                
                Console.WriteLine("[PeekMessagesAsync] Sending peek request to queue...");
                var response = await _queueClient.PeekMessagesAsync(maxMessages);
                Console.WriteLine($"[PeekMessagesAsync] Received response, status: {response.GetRawResponse().Status}");
                
                PeekedMessage[] peeked = response.Value;
                Console.WriteLine($"[PeekMessagesAsync] Found {peeked?.Length ?? 0} messages in queue");

                if (peeked == null || peeked.Length == 0)
                {
                    Console.WriteLine("[PeekMessagesAsync] No messages found in queue");
                    return results;
                }

                foreach (var m in peeked)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(m.MessageText))
                        {
                            Console.WriteLine("[PeekMessagesAsync] Warning: Empty message text found");
                            continue;
                        }
                        
                        Console.WriteLine($"[PeekMessagesAsync] Processing message with length: {m.MessageText.Length} chars");
                        
                        // Check if the message is base64 encoded
                        string json;
                        try 
                        {
                            var bytes = Convert.FromBase64String(m.MessageText);
                            json = Encoding.UTF8.GetString(bytes);
                            Console.WriteLine($"[PeekMessagesAsync] Successfully decoded base64 message");
                        }
                        catch (FormatException)
                        {
                            // Not base64, use as-is
                            Console.WriteLine("[PeekMessagesAsync] Message is not base64 encoded, using as-is");
                            json = m.MessageText;
                        }
                        
                        try
                        {
                            // Try to format the JSON for better readability
                            var parsedJson = System.Text.Json.JsonDocument.Parse(json);
                            json = System.Text.Json.JsonSerializer.Serialize(parsedJson, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                        }
                        catch
                        {
                            // If it's not valid JSON, just use as-is
                            Console.WriteLine("[PeekMessagesAsync] Message is not valid JSON, showing raw");
                        }
                        
                        results.Add(json);
                    }
                    catch (Exception ex)
                    {
                        string errorMsg = $"[PeekMessagesAsync] Error processing message: {ex.Message}";
                        Console.WriteLine(errorMsg);
                        Console.WriteLine($"Message content (first 200 chars): {m.MessageText?.Substring(0, Math.Min(200, m.MessageText?.Length ?? 0))}");
                        
                        // Add error information to results
                        results.Add($"[Error processing message] {errorMsg}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PeekMessagesAsync] Error accessing queue: {ex}");
                throw;
            }
            
            Console.WriteLine($"[PeekMessagesAsync] Returning {results.Count} messages");
            return results;
        }
    }
}