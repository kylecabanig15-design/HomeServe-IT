using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HomeServeIT.Web.Models;

public class Invoice
{
    [Key]
    public int InvoiceID { get; set; }

    [Required]
    public int RequestID { get; set; }

    [ForeignKey("RequestID")]
    public ServiceRequest ServiceRequest { get; set; } = null!;

    [Column(TypeName = "decimal(10,2)")]
    public decimal TotalAmount { get; set; }

    [Required]
    [MaxLength(20)]
    public string PaymentStatus { get; set; } = "Unpaid"; // Unpaid, Paid, Voided

    public DateTime DateIssued { get; set; } = DateTime.UtcNow;

    public bool IsQuotation { get; set; } = false;

    [MaxLength(30)]
    public string QuotationStatus { get; set; } = "Draft"; // Draft, PendingAdmin, Approved, Rejected

    public string? BreakdownDetails { get; set; }

}
