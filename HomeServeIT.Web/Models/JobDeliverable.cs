using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HomeServeIT.Web.Models;

public class JobDeliverable
{
    [Key]
    public int DeliverableID { get; set; }

    [Required]
    public int RequestID { get; set; }

    [ForeignKey("RequestID")]
    public ServiceRequest ServiceRequest { get; set; } = null!;

    [Required]
    [MaxLength(50)]
    public string Phase { get; set; } = "Diagnosis"; // Diagnosis, FinalProof

    [Required]
    public string Description { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? ImagePath { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
