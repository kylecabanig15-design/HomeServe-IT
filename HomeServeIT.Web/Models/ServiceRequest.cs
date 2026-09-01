using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HomeServeIT.Web.Models;

public class ServiceRequest
{
    [Key]
    public int RequestID { get; set; }

    [Required]
    public int CustomerID { get; set; }

    [ForeignKey("CustomerID")]
    public Customer Customer { get; set; } = null!;

    public int? TechID { get; set; }

    [ForeignKey("TechID")]
    public Technician? Technician { get; set; }

    [Required]
    [MaxLength(255)]
    public string IssueDescription { get; set; } = string.Empty;

    public DateTime ScheduledDate { get; set; }

    public DateTime? EstimatedDeadline { get; set; }

    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = "Pending"; // Pending, In Progress, Completed

    [Required]
    [MaxLength(20)]
    public string Priority { get; set; } = "Normal"; // Low, Normal, Urgent

    public DateTime? CompletedDate { get; set; }

    [Required]
    [MaxLength(50)]
    public string ServiceCategory { get; set; } = "Other"; // Hardware Repair, Software / OS, Network Setup, CCTV / Security, Data Recovery, Phone, Other

    [MaxLength(500)]
    public string? ImagePath { get; set; }

    // Archiving
    public bool IsArchived { get; set; } = false;

    // Checklist Progress
    public bool Check1_Diagnostic { get; set; } = false;
    public bool Check2_Hardware { get; set; } = false;
    public bool Check3_Firmware { get; set; } = false;
    public bool Check4_QA { get; set; } = false;
    public bool Check5_Handover { get; set; } = false;

    // Cancellation Workflow
    public bool IsCancellationRequested { get; set; } = false;
    
    [MaxLength(500)]
    public string? CancellationReason { get; set; }
    
    [MaxLength(20)]
    public string? CancellationStatus { get; set; } // "Pending", "Approved", "Rejected"
    
    [MaxLength(500)]
    public string? CancellationRejectReason { get; set; }
}
