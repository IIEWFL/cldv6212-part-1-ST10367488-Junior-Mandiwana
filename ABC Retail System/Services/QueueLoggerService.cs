using ABC_Retail_System.Services.Storage;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Queues;
using Microsoft.Extensions.Logging;

namespace ABC_Retail_System.Services
{
    public class QueueLoggerService : BackgroundService
    {
        private readonly QueueServiceClient _queueServiceClient;
        private readonly ILogger<QueueLoggerService> _logger;
        private const string QueueName = "product-operations";

        public QueueLoggerService(
            QueueServiceClient queueServiceClient,
            ILogger<QueueLoggerService> logger)
        {
            _queueServiceClient = queueServiceClient;
            _logger = logger;
        }

        public async Task LogOperationAsync(string operation, string entityType, string entityId, string details = null)
        {
            try
            {
                var queueClient = _queueServiceClient.GetQueueClient(QueueName);
                await queueClient.CreateIfNotExistsAsync();

                var logMessage = new
                {
                    Timestamp = DateTime.UtcNow,
                    Operation = operation,
                    EntityType = entityType,
                    EntityId = entityId,
                    Details = details
                };

                var message = JsonSerializer.Serialize(logMessage);
                await queueClient.SendMessageAsync(
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(message)));
                
                _logger.LogInformation($"Logged {operation} operation for {entityType} {entityId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to log {operation} operation for {entityType} {entityId}");
                // Don't throw to avoid affecting the main operation
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Queue Logger Service is starting.");

            var queueClient = _queueServiceClient.GetQueueClient(QueueName);
            await queueClient.CreateIfNotExistsAsync(cancellationToken: stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Get messages from the queue
                    var messages = await queueClient.ReceiveMessagesAsync(
                        maxMessages: 10,
                        visibilityTimeout: TimeSpan.FromSeconds(30),
                        cancellationToken: stoppingToken);

                    foreach (var message in messages.Value)
                    {
                        try
                        {
                            // Process the message (you can add additional processing here)
                            var messageText = message.MessageText;
                            if (messageText != null)
                            {
                                // Log the message
                                _logger.LogInformation($"Processing message: {messageText}");
                            }

                            // Delete the message from the queue after processing
                            await queueClient.DeleteMessageAsync(
                                message.MessageId,
                                message.PopReceipt,
                                stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing queue message");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error receiving messages from queue");
                    _logger.LogError(ex, "Error in Queue Logger Service");
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
            }

            _logger.LogInformation("Queue Logger Service is stopping.");
        }
    }
}
