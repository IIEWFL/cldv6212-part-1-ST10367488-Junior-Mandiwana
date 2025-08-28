using Azure;
using Azure.Data.Tables;
using System.ComponentModel.DataAnnotations;

namespace ABC_Retail_System.Models
{
    public class Product : ITableEntity
    {
        // Required by ITableEntity
        public string? PartitionKey { get; set; }   // e.g., "Electronics"
        public string? RowKey { get; set; }         // e.g., "PROD001"
        public DateTimeOffset? Timestamp { get; set; }  // Set automatically by Azure
        public ETag ETag { get; set; }             // Used for concurrency control

        [Required(ErrorMessage = "Product name is required")]
        [Display(Name = "Product Name")]
        public string? ProductName { get; set; }

        [Required(ErrorMessage = "Price is required")]
        [Range(0.01, double.MaxValue, ErrorMessage = "Price must be greater than 0")]
        public double? Price { get; set; }

        [Required(ErrorMessage = "Stock quantity is required")]
        [Range(0, int.MaxValue, ErrorMessage = "Stock quantity cannot be negative")]
        [Display(Name = "Stock Quantity")]
        public int? StockQuantity { get; set; }

        [Display(Name = "Image URL")]
        public string? ImageUrl { get; set; } // URL from Azure Blob Storage

        [Required(ErrorMessage = "Brand is required")]
        public string? Brand { get; set; }

        [Required(ErrorMessage = "Color is required")]
        public string? Colour { get; set; }

        [Required(ErrorMessage = "Size is required")]
        public string? Size { get; set; }
    }
}
