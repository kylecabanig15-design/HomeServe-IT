using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HomeServeIT.Web.Models;

public class StockMovement
{
    [Key]
    public int MovementID { get; set; }

    [Required]
    public int ItemID { get; set; }

    [ForeignKey(nameof(ItemID))]
    public InventoryItem InventoryItem { get; set; } = null!;

    public int? RequestID { get; set; }

    [ForeignKey(nameof(RequestID))]
    public ServiceRequest? ServiceRequest { get; set; }

    [Required]
    [MaxLength(50)]
    public string MovementType { get; set; } = "Job Usage"; // "Job Usage", "Restock", "Initial Stock", "Manual Adjustment", "Return", "Archived"

    /// <summary>
    /// Negative for consumption/usage (e.g. -2), positive for additions/restock (+10).
    /// </summary>
    public int Quantity { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal UnitCost { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal UnitPrice { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [MaxLength(100)]
    public string PerformedBy { get; set; } = string.Empty; // Technician name or Admin

    [MaxLength(255)]
    public string DestinationOrSource { get; set; } = string.Empty; // Where it went or where it came from

    [MaxLength(500)]
    public string? Notes { get; set; }
}
