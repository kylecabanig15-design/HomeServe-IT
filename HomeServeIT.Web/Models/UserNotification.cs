using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HomeServeIT.Web.Models;

public class UserNotification
{
    [Key]
    public int NotificationID { get; set; }

    [Required]
    [MaxLength(255)]
    public string RecipientUserID { get; set; } = string.Empty;

    [ForeignKey(nameof(RecipientUserID))]
    public ApplicationUser RecipientUser { get; set; } = null!;

    [Required]
    [MaxLength(32)]
    public string AudienceRole { get; set; } = string.Empty;

    [Required]
    [MaxLength(40)]
    public string Category { get; set; } = "System";

    [Required]
    [MaxLength(40)]
    public string Icon { get; set; } = "bell";

    [Required]
    [MaxLength(180)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(600)]
    public string Message { get; set; } = string.Empty;

    [Required]
    [MaxLength(300)]
    public string ActionUrl { get; set; } = "/";

    [Required]
    [MaxLength(200)]
    public string SourceKey { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsRead { get; set; }

    public DateTime? ReadAt { get; set; }
}
