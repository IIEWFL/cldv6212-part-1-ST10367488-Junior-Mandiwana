using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ABC_Retail_System.Services.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace ABC_Retail_System.Controllers
{
    public class QueueController : Controller
    {
        private readonly QueueStorageService _queueService;
        private const string QueueName = "product-logs";

        public QueueController(IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("storageConnectionString");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("Storage connection string is not configured. Please check your appsettings.json file.");
            }

            try
            {
                _queueService = new QueueStorageService(connectionString, QueueName);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to initialize queue service. Please check your storage connection string and network connectivity.", ex);
            }
        }

        public async Task<IActionResult> Index(int max = 16)
        {
            var logs = new List<string>(); // store log messages

            try
            {
                void Log(string message)
                {
                    logs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
                    Console.WriteLine(message);
                }

                Log($"Getting messages from queue: {QueueName}");

                // Get message count first
                int messageCount = await _queueService.GetMessageCountAsync();
                ViewBag.MessageCount = messageCount;
                Log($"Queue '{QueueName}' has {messageCount} messages");

                // Ensure max is within reasonable bounds
                max = Math.Clamp(max, 1, 32);
                ViewBag.Max = max;

                // Only try to get messages if there are any
                var messages = new List<string>();
                if (messageCount > 0)
                {
                    Log($"Attempting to peek up to {max} messages...");
                    messages = await _queueService.PeekMessagesAsync(max);
                    Log($"Retrieved {messages?.Count ?? 0} messages");
                }
                else
                {
                    ViewBag.Info = "The queue is currently empty. No messages to display.";
                    Log("The queue is currently empty. No messages to display.");
                }

                ViewBag.Logs = logs; // pass logs to View
                return View(messages ?? new List<string>());
            }
            catch (Exception ex)
            {
                string errorMsg = $"Error accessing queue '{QueueName}': {ex.Message}";
                logs.Add(errorMsg);
                Console.WriteLine($"[QueueController] {errorMsg}");
                Console.WriteLine(ex);

                if (ex.InnerException != null)
                {
                    logs.Add($"Details: {ex.InnerException.Message}");
                }

                ViewBag.Error = errorMsg;
                ViewBag.Logs = logs;
                return View(new List<string>());
            }
        }
    }
}
