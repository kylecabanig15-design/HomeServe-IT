using HomeServeIT.Web.Constants;
using HomeServeIT.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HomeServeIT.Web.Tests;

public sealed class RoleFixtureTests
{
    [Fact]
    public async Task SeedRoleAccounts_CreatesAllIdentityRolesAndDomainProfiles()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext();

        var seeded = await TestDataBuilder.SeedRoleAccountsAsync(context);

        var roles = await context.Roles.Select(role => role.Name).ToListAsync();
        Assert.Contains(Roles.Administrator, roles);
        Assert.Contains(Roles.Customer, roles);
        Assert.Contains(Roles.Technician, roles);
        Assert.Equal(seeded.CustomerUser.Id, seeded.Customer.UserID);
        Assert.Equal(seeded.TechnicianUser.Id, seeded.Technician.UserID);

        var administrator = TestDataBuilder.CreatePrincipal(seeded.Administrator, Roles.Administrator);
        var customer = TestDataBuilder.CreatePrincipal(seeded.CustomerUser, Roles.Customer);
        var technician = TestDataBuilder.CreatePrincipal(seeded.TechnicianUser, Roles.Technician);
        Assert.True(administrator.IsInRole(Roles.Administrator));
        Assert.True(customer.IsInRole(Roles.Customer));
        Assert.True(technician.IsInRole(Roles.Technician));
        Assert.False(customer.IsInRole(Roles.Administrator));
    }
}
