using HomeServeIT.Web.Areas.Admin.Models;
using HomeServeIT.Web.Models;
using HomeServeIT.Web.Services;
using HomeServeIT.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HomeServeIT.Web.Tests;

public sealed class ApplicationSettingsServiceTests
{
    [Fact]
    public async Task SettingsPersistAffectBehaviorAndCreateAnAuditWithoutValues()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var fixture = await TestDataBuilder.SeedRoleAccountsAsync(context);
        var service = new ApplicationSettingsService(context);
        var initial = await service.GetAsync();

        var general = await service.UpdateGeneralAsync(new GeneralSettingsInput
        {
            SystemName = "HomeServe Test Lab",
            SupportEmail = "help@test.invalid",
            ConcurrencyStamp = initial.ConcurrencyStamp
        }, fixture.Administrator.Id);
        Assert.True(general.Succeeded);

        var afterGeneral = await service.GetAsync();
        Assert.True((await service.UpdateCompanyAsync(new CompanySettingsInput
        {
            CompanyName = "University Capstone",
            CompanyAddress = "Davao City",
            ConcurrencyStamp = afterGeneral.ConcurrencyStamp
        }, fixture.Administrator.Id)).Succeeded);

        var afterCompany = await service.GetAsync();
        Assert.True((await service.UpdateNotificationsAsync(new NotificationSettingsInput
        {
            RefreshEnabled = false,
            RefreshIntervalSeconds = 45,
            ConcurrencyStamp = afterCompany.ConcurrencyStamp
        }, fixture.Administrator.Id)).Succeeded);

        var afterNotifications = await service.GetAsync();
        var security = await service.UpdateSecurityAsync(new SecuritySettingsInput
        {
            AllowPublicRegistration = false,
            ConcurrencyStamp = afterNotifications.ConcurrencyStamp
        }, fixture.Administrator.Id);
        Assert.True(security.Succeeded);

        await using var verification = database.CreateContext();
        var saved = await verification.ApplicationSettings.SingleAsync();
        Assert.Equal("HomeServe Test Lab", saved.SystemName);
        Assert.Equal("help@test.invalid", saved.SupportEmail);
        Assert.Equal("University Capstone", saved.CompanyName);
        Assert.Equal("Davao City", saved.CompanyAddress);
        Assert.False(saved.NotificationRefreshEnabled);
        Assert.Equal(45, saved.NotificationRefreshIntervalSeconds);
        Assert.False(saved.AllowPublicRegistration);
        var audits = await verification.ApplicationSettingAudits.OrderBy(a => a.AuditId).ToListAsync();
        Assert.Equal(4, audits.Count);
        Assert.All(audits, audit => Assert.Equal(fixture.Administrator.Id, audit.ActorUserId));
        Assert.DoesNotContain("HomeServe Test Lab", string.Join(',', audits.Select(a => a.ChangedFields)));
    }

    [Fact]
    public async Task StaleSettingsUpdateIsRejected()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var fixture = await TestDataBuilder.SeedRoleAccountsAsync(context);
        var service = new ApplicationSettingsService(context);
        var stale = await service.GetAsync();
        Assert.True((await service.UpdateCompanyAsync(new CompanySettingsInput
        {
            CompanyName = "First update",
            CompanyAddress = "Davao City",
            ConcurrencyStamp = stale.ConcurrencyStamp
        }, fixture.Administrator.Id)).Succeeded);

        var conflict = await service.UpdateNotificationsAsync(new NotificationSettingsInput
        {
            RefreshEnabled = false,
            RefreshIntervalSeconds = 60,
            ConcurrencyStamp = stale.ConcurrencyStamp
        }, fixture.Administrator.Id);

        Assert.False(conflict.Succeeded);
        Assert.True(conflict.Conflict);
        Assert.True((await service.GetAsync()).NotificationRefreshEnabled);
    }
}
