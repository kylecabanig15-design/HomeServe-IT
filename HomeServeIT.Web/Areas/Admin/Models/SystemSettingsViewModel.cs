using System.ComponentModel.DataAnnotations;

namespace HomeServeIT.Web.Areas.Admin.Models;

public sealed class SystemSettingsViewModel
{
    public string ActiveTab { get; set; } = "general";
    public GeneralSettingsInput General { get; set; } = new();
    public CompanySettingsInput Company { get; set; } = new();
    public NotificationSettingsInput Notifications { get; set; } = new();
    public SecuritySettingsInput Security { get; set; } = new();
}

public abstract class SettingsInput
{
    public string ConcurrencyStamp { get; set; } = "";
}

public sealed class GeneralSettingsInput : SettingsInput
{
    [Required, StringLength(80, MinimumLength = 2)]
    [Display(Name = "System name")]
    public string SystemName { get; set; } = "";

    [Required, EmailAddress, StringLength(254)]
    [Display(Name = "Support email")]
    public string SupportEmail { get; set; } = "";
}

public sealed class CompanySettingsInput : SettingsInput
{
    [Required, StringLength(120, MinimumLength = 2)]
    [Display(Name = "Company or project name")]
    public string CompanyName { get; set; } = "";

    [Required, StringLength(250, MinimumLength = 2)]
    [Display(Name = "Company or project address")]
    public string CompanyAddress { get; set; } = "";
}

public sealed class NotificationSettingsInput : SettingsInput
{
    [Display(Name = "Enable notification refresh")]
    public bool RefreshEnabled { get; set; }

    [Range(15, 3600)]
    [Display(Name = "Refresh interval (seconds)")]
    public int RefreshIntervalSeconds { get; set; } = 30;
}

public sealed class SecuritySettingsInput : SettingsInput
{
    [Display(Name = "Allow public customer registration")]
    public bool AllowPublicRegistration { get; set; }
}
