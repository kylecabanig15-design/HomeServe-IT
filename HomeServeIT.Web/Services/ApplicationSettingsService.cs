using HomeServeIT.Web.Areas.Admin.Models;
using HomeServeIT.Web.Data;
using HomeServeIT.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeServeIT.Web.Services;

public sealed record ApplicationSettingsSnapshot(
    string SystemName,
    string SupportEmail,
    string CompanyName,
    string CompanyAddress,
    bool NotificationRefreshEnabled,
    int NotificationRefreshIntervalSeconds,
    bool AllowPublicRegistration,
    string ConcurrencyStamp);

public sealed record SettingsUpdateResult(bool Succeeded, bool Conflict = false);

public sealed class ApplicationSettingsService(ApplicationDbContext context)
{
    public async Task<ApplicationSettingsSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        var setting = await context.ApplicationSettings.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == ApplicationSetting.SingletonId, cancellationToken);
        return Snapshot(setting ?? Defaults());
    }

    public async Task<SystemSettingsViewModel> GetPageAsync(string? activeTab = null, CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken);
        return new SystemSettingsViewModel
        {
            ActiveTab = NormalizeTab(activeTab),
            General = new GeneralSettingsInput { SystemName = settings.SystemName, SupportEmail = settings.SupportEmail, ConcurrencyStamp = settings.ConcurrencyStamp },
            Company = new CompanySettingsInput { CompanyName = settings.CompanyName, CompanyAddress = settings.CompanyAddress, ConcurrencyStamp = settings.ConcurrencyStamp },
            Notifications = new NotificationSettingsInput { RefreshEnabled = settings.NotificationRefreshEnabled, RefreshIntervalSeconds = settings.NotificationRefreshIntervalSeconds, ConcurrencyStamp = settings.ConcurrencyStamp },
            Security = new SecuritySettingsInput { AllowPublicRegistration = settings.AllowPublicRegistration, ConcurrencyStamp = settings.ConcurrencyStamp }
        };
    }

    public Task<SettingsUpdateResult> UpdateGeneralAsync(GeneralSettingsInput input, string actorUserId, CancellationToken cancellationToken = default) =>
        UpdateAsync(input.ConcurrencyStamp, "General", "SystemName,SupportEmail", actorUserId, setting =>
        {
            setting.SystemName = input.SystemName.Trim();
            setting.SupportEmail = input.SupportEmail.Trim();
        }, cancellationToken);

    public Task<SettingsUpdateResult> UpdateCompanyAsync(CompanySettingsInput input, string actorUserId, CancellationToken cancellationToken = default) =>
        UpdateAsync(input.ConcurrencyStamp, "Company", "CompanyName,CompanyAddress", actorUserId, setting =>
        {
            setting.CompanyName = input.CompanyName.Trim();
            setting.CompanyAddress = input.CompanyAddress.Trim();
        }, cancellationToken);

    public Task<SettingsUpdateResult> UpdateNotificationsAsync(NotificationSettingsInput input, string actorUserId, CancellationToken cancellationToken = default) =>
        UpdateAsync(input.ConcurrencyStamp, "Notifications", "NotificationRefreshEnabled,NotificationRefreshIntervalSeconds", actorUserId, setting =>
        {
            setting.NotificationRefreshEnabled = input.RefreshEnabled;
            setting.NotificationRefreshIntervalSeconds = input.RefreshIntervalSeconds;
        }, cancellationToken);

    public Task<SettingsUpdateResult> UpdateSecurityAsync(SecuritySettingsInput input, string actorUserId, CancellationToken cancellationToken = default) =>
        UpdateAsync(input.ConcurrencyStamp, "Security", "AllowPublicRegistration", actorUserId,
            setting => setting.AllowPublicRegistration = input.AllowPublicRegistration, cancellationToken);

    private async Task<SettingsUpdateResult> UpdateAsync(string expectedStamp, string section, string changedFields,
        string actorUserId, Action<ApplicationSetting> update, CancellationToken cancellationToken)
    {
        var setting = await context.ApplicationSettings.SingleOrDefaultAsync(s => s.Id == ApplicationSetting.SingletonId, cancellationToken);
        if (setting == null)
        {
            setting = Defaults();
            context.ApplicationSettings.Add(setting);
        }
        else if (!string.Equals(setting.ConcurrencyStamp, expectedStamp, StringComparison.Ordinal))
        {
            return new SettingsUpdateResult(false, true);
        }

        update(setting);
        setting.UpdatedAtUtc = DateTime.UtcNow;
        setting.UpdatedByUserId = actorUserId;
        setting.ConcurrencyStamp = Guid.NewGuid().ToString("D");
        context.ApplicationSettingAudits.Add(new ApplicationSettingAudit
        {
            Section = section,
            ChangedFields = changedFields,
            ActorUserId = actorUserId,
            ChangedAtUtc = setting.UpdatedAtUtc
        });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return new SettingsUpdateResult(true);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new SettingsUpdateResult(false, true);
        }
        catch (DbUpdateException) when (context.Entry(setting).State == EntityState.Added)
        {
            return new SettingsUpdateResult(false, true);
        }
    }

    private static ApplicationSetting Defaults() => new();
    private static ApplicationSettingsSnapshot Snapshot(ApplicationSetting setting) => new(
        setting.SystemName, setting.SupportEmail, setting.CompanyName, setting.CompanyAddress,
        setting.NotificationRefreshEnabled, setting.NotificationRefreshIntervalSeconds,
        setting.AllowPublicRegistration, setting.ConcurrencyStamp);

    private static string NormalizeTab(string? tab) => tab is "general" or "company" or "notifications" or "security" or "integrations"
        ? tab : "general";
}
