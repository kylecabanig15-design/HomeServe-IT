using HomeServeIT.Web.Constants;
using HomeServeIT.Web.Data;
using HomeServeIT.Web.Models;
using HomeServeIT.Web.Services;
using HomeServeIT.Web.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HomeServeIT.Web.Tests;

public sealed class AccountProfileServiceTests
{
    private static ServiceProvider Services(SqliteTestDatabase database)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddScoped(_ => database.CreateContext());
        services.AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
        services.AddScoped<AccountProfileService>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task AddTechnicianCreatesUsablePasswordAndAllowsOptionalChange()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var provider = Services(database);
        await using var scope = provider.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        await roles.CreateAsync(new IdentityRole(Roles.Technician));
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var controller = new HomeServeIT.Web.Areas.Admin.Controllers.OperationsController(context, null!, null!, null!)
        {
            TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
                new Microsoft.AspNetCore.Http.DefaultHttpContext(), new TestTempDataProvider())
        };
        var result = await controller.AddTechnician(users, "New", "Technician", "newtech@test.invalid",
            "+639123456789", "Plumbing", scope.ServiceProvider.GetRequiredService<AccountProfileService>());
        Assert.Equal("Technicians", Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectToActionResult>(result).ActionName);
        var password = Assert.IsType<string>(controller.TempData["CreatedTechnicianPassword"]);
        var user = (await users.FindByEmailAsync("newtech@test.invalid"))!;
        Assert.True(await users.CheckPasswordAsync(user, password));
        Assert.True(await users.IsInRoleAsync(user, Roles.Technician));
        Assert.True(await context.Technicians.AnyAsync(t => t.UserID == user.Id));
        Assert.True((await users.ChangePasswordAsync(user, password, "OptionalChange!456")).Succeeded);
        Assert.False(await users.CheckPasswordAsync(user, password));
        Assert.True(await users.CheckPasswordAsync(user, "OptionalChange!456"));
    }

    private sealed class TestTempDataProvider : Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(Microsoft.AspNetCore.Http.HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(Microsoft.AspNetCore.Http.HttpContext context, IDictionary<string, object> values) { }
    }

    [Theory]
    [InlineData(Roles.Customer)]
    [InlineData(Roles.Technician)]
    public async Task ProfileChangesPersistInIdentityAndDomainAndInvitationHasSingleUseSetup(string role)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var provider = Services(database);
        await using var scope = provider.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        Assert.True((await roles.CreateAsync(new IdentityRole(role))).Succeeded);
        var profiles = scope.ServiceProvider.GetRequiredService<AccountProfileService>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { FullName = "Initial Name", Email = "profile@test.invalid", UserName = "profile@test.invalid" };
        Assert.True((await profiles.CreateAsync(user, role)).Succeeded);
        Assert.False(await users.HasPasswordAsync(user));
        var token = await users.GeneratePasswordResetTokenAsync(user);
        Assert.True((await users.ResetPasswordAsync(user, token, "TestOnly@123")).Succeeded);
        Assert.False((await users.ResetPasswordAsync(user, token, "Changed@123")).Succeeded);
        var update = new ProfileViewModel { FullName = "Renamed Person", Email = "renamed@test.invalid",
            Mobile = "+639123456789", Address = "New Street", City = "New City" };
        Assert.True((await profiles.UpdateAsync(user, update)).Succeeded);
        await using var verification = database.CreateContext();
        var saved = await verification.Users.SingleAsync();
        Assert.Equal(update.Email, saved.Email);
        Assert.Equal(update.FullName, saved.FullName);
        if (role == Roles.Customer)
        {
            var customer = await verification.Customers.SingleAsync();
            Assert.Equal("Renamed", customer.FirstName);
            Assert.Equal("Person", customer.LastName);
            Assert.Equal(update.Mobile, customer.PhoneNumber);
            Assert.Equal("New Street, New City", customer.HomeAddress);
        }
        else Assert.Equal("Renamed", (await verification.Technicians.SingleAsync()).FirstName);
    }

    [Fact]
    public async Task CollisionRejectsEntireProfileChange()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var provider = Services(database);
        await using var scope = provider.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        await roles.CreateAsync(new IdentityRole(Roles.Customer));
        var profiles = scope.ServiceProvider.GetRequiredService<AccountProfileService>();
        var first = new ApplicationUser { FullName = "First Person", Email = "first@test.invalid", UserName = "first@test.invalid" };
        var second = new ApplicationUser { FullName = "Second Person", Email = "second@test.invalid", UserName = "second@test.invalid" };
        Assert.True((await profiles.CreateAsync(first, Roles.Customer)).Succeeded);
        Assert.True((await profiles.CreateAsync(second, Roles.Customer)).Succeeded);
        Assert.False((await profiles.UpdateAsync(second, new ProfileViewModel { FullName = "Wrong Person", Email = first.Email! })).Succeeded);
        await using var verification = database.CreateContext();
        Assert.Equal("Second Person", (await verification.Users.SingleAsync(u => u.Id == second.Id)).FullName);
        Assert.Equal("Second", (await verification.Customers.SingleAsync(c => c.UserID == second.Id)).FirstName);
    }

    [Fact]
    public async Task NotificationReadsDoNotWriteAndSynchronizationIsIdempotent()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var provider = Services(database);
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var fixture = await TestDataBuilder.SeedRoleAccountsAsync(context);
        await TestDataBuilder.AddRequestAsync(context, fixture.Customer.CustomerID, DateTime.UtcNow);
        var service = new NotificationService(context, scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>());
        Assert.Empty((await service.GetFeedAsync(fixture.CustomerUser)).Notifications);
        Assert.Empty(await context.UserNotifications.ToListAsync());
        await service.SynchronizeUserAsync(fixture.CustomerUser.Id);
        Assert.Single((await service.GetFeedAsync(fixture.CustomerUser)).Notifications);
        await service.MarkAllReadAsync(fixture.CustomerUser);
        await service.SynchronizeUserAsync(fixture.CustomerUser.Id);
        Assert.Single(await context.UserNotifications.ToListAsync());
        Assert.Equal(0, (await service.GetFeedAsync(fixture.CustomerUser)).UnreadCount);
    }

    [Fact]
    public async Task InvalidPasswordRollsBackEmailRoleAndDomainChanges()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var provider = Services(database);
        await using var scope = provider.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        await roles.CreateAsync(new IdentityRole(Roles.Customer));
        await roles.CreateAsync(new IdentityRole(Roles.Technician));
        var profiles = scope.ServiceProvider.GetRequiredService<AccountProfileService>();
        var user = new ApplicationUser { FullName = "Original Name", Email = "original@test.invalid", UserName = "original@test.invalid" };
        Assert.True((await profiles.CreateAsync(user, Roles.Customer)).Succeeded);
        var result = await profiles.UpdateAsync(user, new ProfileViewModel { FullName = "Wrong Name", Email = "wrong@test.invalid" }, Roles.Technician, "weak");
        Assert.False(result.Succeeded);
        await using var verification = database.CreateContext();
        Assert.Equal("original@test.invalid", (await verification.Users.SingleAsync()).Email);
        Assert.Equal("Original", (await verification.Customers.SingleAsync()).FirstName);
        Assert.Empty(await verification.Technicians.ToListAsync());
        var roleId = (await verification.UserRoles.SingleAsync()).RoleId;
        Assert.Equal(Roles.Customer, (await verification.Roles.SingleAsync(r => r.Id == roleId)).Name);
    }

    [Fact]
    public async Task FailedRoleAssignmentRollsBackNewIdentityAccount()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var provider = Services(database);
        await using var scope = provider.CreateAsyncScope();
        var profiles = scope.ServiceProvider.GetRequiredService<AccountProfileService>();
        var user = new ApplicationUser { FullName = "Missing Role", Email = "missing@test.invalid", UserName = "missing@test.invalid" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => profiles.CreateAsync(user, Roles.Technician));
        await using var verification = database.CreateContext();
        Assert.Empty(await verification.Users.ToListAsync());
        Assert.Empty(await verification.Technicians.ToListAsync());
    }
}
