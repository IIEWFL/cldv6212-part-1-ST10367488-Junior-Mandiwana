using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace ABC_Retail_System.Services.Storage
{
    public class BlobStorageService
    {
        private readonly BlobContainerClient _blobContainerClient;

        public BlobStorageService(string storageConnectionString, string containerName)
        {
            var serviceClient = new BlobServiceClient(storageConnectionString);
            _blobContainerClient = serviceClient.GetBlobContainerClient(containerName);
            _blobContainerClient.CreateIfNotExists();
        }

        public async Task<string> UploadAsync(string blobName, Stream content, string? contentType = null)
        {
            BlobClient blobClient = _blobContainerClient.GetBlobClient(blobName);
            var options = new BlobUploadOptions();
            if (!string.IsNullOrWhiteSpace(contentType))
            {
                options.HttpHeaders = new BlobHttpHeaders { ContentType = contentType };
            }
            await blobClient.UploadAsync(content, options);
            if (blobClient.CanGenerateSasUri)
            {
                var sasBuilder = new BlobSasBuilder
                {
                    BlobContainerName = _blobContainerClient.Name,
                    BlobName = blobName,
                    Resource = "b",
                    StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5),
                    ExpiresOn = DateTimeOffset.UtcNow.AddDays(7)
                };
                sasBuilder.SetPermissions(BlobSasPermissions.Read);
                Uri sasUri = blobClient.GenerateSasUri(sasBuilder);
                return sasUri.ToString();
            }
            return blobClient.Uri.ToString();
        }

        // Convenience wrappers to match controller usage
        public Task<string> UploadPhotoAsync(string baseName, Stream content)
        {
            string blobName = string.IsNullOrWhiteSpace(Path.GetExtension(baseName))
                ? baseName + ".jpg"
                : baseName;
            return UploadAsync(blobName, content, "image/jpeg");
        }

        public async Task DeletePhotoAsync(string blobName)
        {
            if (string.IsNullOrWhiteSpace(blobName)) return;
            // Try exact name
            await _blobContainerClient.DeleteBlobIfExistsAsync(blobName);
            // Also try with .jpg if not provided
            if (!blobName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                await _blobContainerClient.DeleteBlobIfExistsAsync(blobName + ".jpg");
            }
        }
    }
}
