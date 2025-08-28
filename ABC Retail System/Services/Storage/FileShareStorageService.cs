using Azure;
using Azure.Storage.Files.Shares;
using Azure.Storage.Files.Shares.Models;
using Azure.Storage.Sas;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace ABC_Retail_System.Services.Storage
{
    public class FileShareStorageService
    {
        private readonly ShareClient _shareClient;
        private readonly string _shareName;

        public FileShareStorageService(string storageConnectionString, string shareName)
        {
            if (string.IsNullOrEmpty(storageConnectionString))
                throw new ArgumentNullException(nameof(storageConnectionString));
            if (string.IsNullOrEmpty(shareName))
                throw new ArgumentNullException(nameof(shareName));

            _shareName = shareName;
            var service = new ShareServiceClient(storageConnectionString);
            _shareClient = service.GetShareClient(shareName);
        }

        public async Task InitializeAsync()
        {
            try
            {
                // Create the share if it doesn't exist
                await _shareClient.CreateIfNotExistsAsync();
                
                // Ensure the logs directory exists
                var logsDir = _shareClient.GetDirectoryClient("logs");
                await logsDir.CreateIfNotExistsAsync();
                
                Console.WriteLine($"[FileShare] Initialized share '{_shareName}' with logs directory");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FileShare] ERROR initializing share '{_shareName}': {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"[FileShare] Inner exception: {ex.InnerException.Message}");
                }
                throw;
            }
        }
        
        public async Task SaveQueueMessageAsync(string message, string queueName)
        {
            if (string.IsNullOrEmpty(message))
                throw new ArgumentNullException(nameof(message));
                
            if (string.IsNullOrEmpty(queueName))
                throw new ArgumentNullException(nameof(queueName));
                
            try
            {
                Console.WriteLine($"[FileShare] Saving message to queue: {queueName}");
                
                // Ensure the logs directory exists
                var logsDir = _shareClient.GetDirectoryClient("logs");
                await logsDir.CreateIfNotExistsAsync();
                
                // Create a queue-specific subdirectory
                var queueDir = logsDir.GetSubdirectoryClient(queueName);
                await queueDir.CreateIfNotExistsAsync();
                
                // Create a file with timestamp in the filename
                var timestamp = DateTime.UtcNow;
                var fileName = $"{timestamp:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}.log";
                var file = queueDir.GetFileClient(fileName);
                
                // Add timestamp to the message
                var logMessage = $"[{timestamp:o}] {message}\n";
                var bytes = System.Text.Encoding.UTF8.GetBytes(logMessage);
                
                using var stream = new MemoryStream(bytes);
                
                // First create the file with the correct size
                await file.CreateAsync(stream.Length);
                
                // Then upload the content
                await file.UploadRangeAsync(
                    new Azure.HttpRange(0, stream.Length),
                    stream);
                
                Console.WriteLine($"[FileShare] Successfully saved message to {queueName}/{fileName} (Size: {stream.Length} bytes)");
                return;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                Console.WriteLine($"[FileShare] ERROR: The file share '{_shareName}' was not found. Please check if the share exists and the connection string is correct.");
                throw new InvalidOperationException($"File share '{_shareName}' not found. {ex.Message}", ex);
            }
            catch (RequestFailedException ex) when (ex.Status == 403)
            {
                Console.WriteLine($"[FileShare] ERROR: Access denied to file share '{_shareName}'. Please check your storage account credentials.");
                throw new UnauthorizedAccessException($"Access denied to file share '{_shareName}'. {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FileShare] ERROR saving queue message to {queueName}: {ex}");
                throw new InvalidOperationException($"Failed to save message to file share. {ex.Message}", ex);
            }
        }
        
        public async Task<List<string>> GetQueueMessagesAsync(string queueName, int maxMessages = 50)
        {
            var messages = new List<string>();
            
            if (string.IsNullOrEmpty(queueName))
                throw new ArgumentNullException(nameof(queueName));
                
            try
            {
                Console.WriteLine($"[FileShare] Retrieving messages from queue: {queueName}");
                
                var logsDir = _shareClient.GetDirectoryClient("logs");
                if (!await logsDir.ExistsAsync())
                {
                    Console.WriteLine($"[FileShare] No logs directory found for queue: {queueName}");
                    return messages;
                }
                
                var queueDir = logsDir.GetSubdirectoryClient(queueName);
                if (!await queueDir.ExistsAsync())
                {
                    Console.WriteLine($"[FileShare] No directory found for queue: {queueName}");
                    return messages;
                }
                
                // Get all files in the queue directory, sorted by creation time (newest first)
                var files = queueDir.GetFilesAndDirectories()
                    .OrderByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(maxMessages);
                
                foreach (var file in files)
                {
                    var fileClient = queueDir.GetFileClient(file.Name);
                    var response = await fileClient.DownloadAsync();
                    using var streamReader = new StreamReader(response.Value.Content);
                    var content = await streamReader.ReadToEndAsync();
                    messages.Add($"{file.Name}: {content.Trim()}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FileShare] ERROR retrieving queue messages: {ex.Message}");
                throw;
            }
            
            return messages;
        }

        // Upload a file into the root directory
        public async Task UpLoadFile(string fileName, Stream fileStream)
        {
            var dir = _shareClient.GetRootDirectoryClient(); // root already exists
            var file = dir.GetFileClient(fileName);

            // Create the file with the correct length, then upload ranges
            await file.CreateAsync(fileStream.Length);

            const int chunk = 4 * 1024 * 1024; // 4 MB
            long pos = 0;
            byte[] buffer = new byte[chunk];
            int read;
            while ((read = await fileStream.ReadAsync(buffer, 0, chunk)) > 0)
            {
                using var ms = new MemoryStream(buffer, 0, read);
                await file.UploadRangeAsync(
                    ShareFileRangeWriteType.Update,
                    new HttpRange(pos, read),
                    ms
                );
                pos += read;
            }
        }

        // List files in the root directory
        public async Task<List<string>> ListFilesAsync()
        {
            var dir = _shareClient.GetRootDirectoryClient(); // root already exists
            var names = new List<string>();

            await foreach (ShareFileItem item in dir.GetFilesAndDirectoriesAsync())
            {
                if (!item.IsDirectory) names.Add(item.Name);
            }
            return names;
        }

        // Optional: delete a file (useful if you added the delete button)
        public async Task<bool> DeleteFileAsync(string fileName)
        {
            var dir = _shareClient.GetRootDirectoryClient();
            var file = dir.GetFileClient(fileName);
            var resp = await file.DeleteIfExistsAsync();
            return resp.Value;
        }

        // Generate a read-only SAS link to download the file
        public string GetFileSasUri(string fileName, int validMinutes = 60)
        {
            var dir = _shareClient.GetRootDirectoryClient();
            var file = dir.GetFileClient(fileName);

            if (!file.CanGenerateSasUri)
                throw new InvalidOperationException(
                    "Cannot generate SAS from FileClient. Ensure your connection string uses an account key (not a SAS).");

            var sas = new ShareSasBuilder
            {
                ShareName = _shareClient.Name,
                FilePath = fileName,
                Resource = "f",
                ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(validMinutes)
            };
            sas.SetPermissions(ShareFileSasPermissions.Read);

            return file.GenerateSasUri(sas).ToString();
        }
    }
}