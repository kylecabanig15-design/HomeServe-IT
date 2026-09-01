using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HomeServeIT.Web.Models;

public class JobInventoryUsage
{
    [Key]
    public int UsageID { get; set; }
    public int RequestID { get; set; }
    public int ItemID { get; set; }
    public int Quantity { get; set; }
    [Column(TypeName = "decimal(10,2)")]
    public decimal UnitPrice { get; set; }
    public bool IsDeducted { get; set; }

    [ForeignKey(nameof(RequestID))]
    public ServiceRequest ServiceRequest { get; set; } = null!;
    [ForeignKey(nameof(ItemID))]
    public InventoryItem InventoryItem { get; set; } = null!;
}
