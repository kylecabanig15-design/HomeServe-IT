using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HomeServeIT.Web.Models;

public class InventoryItem
{
    [Key]
    public int ItemID { get; set; }

    [Required]
    [MaxLength(100)]
    public string ItemName { get; set; } = string.Empty;

    public int StockQuantity { get; set; } = 0;

    [MaxLength(50)]
    public string SKU { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Category { get; set; } = string.Empty;

    [Column(TypeName = "decimal(10,2)")]
    public decimal UnitCost { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal UnitPrice { get; set; }

    public int ReorderLevel { get; set; } = 0;
    
    [MaxLength(2000)]
    public string? ImageUrl { get; set; }
}
