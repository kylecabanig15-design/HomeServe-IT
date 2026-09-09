using System.ComponentModel.DataAnnotations;

namespace HomeServeIT.Web.Models;

public sealed class ApplicationSetting
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    [MaxLength(80)]
    public string SystemName { get; set; } = "HomeServe IT";

    [MaxLength(254)]
    public string SupportEmail { get; set; } = "support@homeserveit.ph";

    [MaxLength(120)]
    public string CompanyName { get; set; } = "HomeServe IT Solutions";

    [MaxLength(250)]
    public string CompanyAddress { get; set; } = "Davao City, Philippines";

    public bool NotificationRefreshEnabled { get; set; } = true;
    public int NotificationRefreshIntervalSeconds { get; set; } = 30;
    public bool AllowPublicRegistration { get; set; } = true;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? UpdatedByUserId { get; set; }

    [MaxLength(36)]
    [ConcurrencyCheck]
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("D");
}
