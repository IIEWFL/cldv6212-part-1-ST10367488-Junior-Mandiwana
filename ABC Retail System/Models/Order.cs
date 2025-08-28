using Azure;
using Azure.Data.Tables;
using System.ComponentModel.DataAnnotations;

namespace ABC_Retail_System.Models
{
    public class Order : ITableEntity
    {
        public string PartitionKey { get; set; } = "Orders";
        public string RowKey { get; set; } = Guid.NewGuid().ToString();
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        [Required]
        [Display(Name = "Customer")]
        public string? CustomerRowKey { get; set; }

        [Required]
        [Display(Name = "Product")]
        public string? ProductRowKey { get; set; }

        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "Quantity must be at least 1")]
        public int Quantity { get; set; }

        [Required]
        public string Status { get; set; } = "Pending";

        // Navigation properties (not stored in Table Storage)
        public string? CustomerName { get; set; }
        public string? ProductName { get; set; }
        public double? ProductPrice { get; set; }
        public double? TotalPrice => ProductPrice * Quantity;
    }
}
