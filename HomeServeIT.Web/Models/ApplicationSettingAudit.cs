using System.ComponentModel.DataAnnotations;

namespace HomeServeIT.Web.Models;

public sealed class ApplicationSettingAudit
{
    public long AuditId { get; set; }

    [MaxLength(32)]
    public string Section { get; set; } = "";

    [MaxLength(500)]
    public string ChangedFields { get; set; } = "";

    [MaxLength(450)]
    public string ActorUserId { get; set; } = "";

    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
}
