using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HomeServeIT.Web.Models;

public class SupportTicket
{
    [Key] public int SupportTicketID { get; set; }
    public int CustomerID { get; set; }
    [ForeignKey(nameof(CustomerID))] public Customer Customer { get; set; } = null!;
    [Required, MaxLength(120)] public string Subject { get; set; } = string.Empty;
    [Required, MaxLength(2000)] public string Message { get; set; } = string.Empty;
    [MaxLength(2000)] public string? AdminResponse { get; set; }
    [Required, MaxLength(50)] public string Status { get; set; } = "Open";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RespondedAt { get; set; }
}
