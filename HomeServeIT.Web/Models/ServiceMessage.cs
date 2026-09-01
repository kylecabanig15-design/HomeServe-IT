using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HomeServeIT.Web.Models;

public class ServiceMessage
{
    [Key]
    public int MessageID { get; set; }

    [Required]
    public int RequestID { get; set; }

    [ForeignKey("RequestID")]
    public ServiceRequest ServiceRequest { get; set; } = null!;

    [Required]
    public string SenderID { get; set; } = null!;

    [ForeignKey("SenderID")]
    public ApplicationUser Sender { get; set; } = null!;

    [Required]
    public string Content { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public bool IsRead { get; set; } = false;
}
