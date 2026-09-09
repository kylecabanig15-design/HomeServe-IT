using System.Security.Claims;
using HomeServeIT.Web.Constants;
using HomeServeIT.Web.Data;
using HomeServeIT.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HomeServeIT.Web.Tests.Infrastructure;

internal sealed record RoleTestData(
    ApplicationUser Administrator,
    ApplicationUser CustomerUser,
    Customer Customer,
    ApplicationUser TechnicianUser,
    Technician Technician);

internal static class TestDataBuilder
{
    public const string TestPassword = "TestOnly@123";

    public static async Task<RoleTestData> SeedRoleAccountsAsync(ApplicationDbContext context)
    {
        var administratorRole = CreateRole("test-role-administrator", Roles.Administrator);
        var customerRole = CreateRole("test-role-customer", Roles.Customer);
        var technicianRole = CreateRole("test-role-technician", Roles.Technician);
        context.Roles.AddRange(administratorRole, customerRole, technicianRole);

        var administrator = CreateUser("test-user-administrator", "admin@test.invalid", "Test Administrator", Roles.Administrator);
        var customerUser = CreateUser("test-user-customer", "customer@test.invalid", "Test Customer", Roles.Customer);
        var technicianUser = CreateUser("test-user-technician", "technician@test.invalid", "Test Technician", Roles.Technician);
        context.Users.AddRange(administrator, customerUser, technicianUser);

        context.UserRoles.AddRange(
            new IdentityUserRole<string> { UserId = administrator.Id, RoleId = administratorRole.Id },
            new IdentityUserRole<string> { UserId = customerUser.Id, RoleId = customerRole.Id },
            new IdentityUserRole<string> { UserId = technicianUser.Id, RoleId = technicianRole.Id });

        var customer = new Customer
        {
            UserID = customerUser.Id,
            FirstName = "Test",
            LastName = "Customer",
            PhoneNumber = "+639000000001",
            HomeAddress = "Test Address"
        };
        var technician = new Technician
        {
            UserID = technicianUser.Id,
            FirstName = "Test",
            LastName = "Technician",
            Specialty = "Integration Testing",
            IsAvailable = true
        };
        context.Customers.Add(customer);
        context.Technicians.Add(technician);
        await context.SaveChangesAsync();

        return new RoleTestData(administrator, customerUser, customer, technicianUser, technician);
    }

    public static ClaimsPrincipal CreatePrincipal(ApplicationUser user, string role) => new(
        new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.Email ?? user.Id),
            new Claim(ClaimTypes.Role, role)
        ],
        authenticationType: "HomeServeTest"));

    public static async Task<Customer> AddCustomerAsync(ApplicationDbContext context)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var user = CreateUser($"customer-{suffix}", $"customer-{suffix}@test.invalid", "Scenario Customer", Roles.Customer);
        var customer = new Customer
        {
            User = user,
            FirstName = "Scenario",
            LastName = "Customer",
            PhoneNumber = "+639000000002",
            HomeAddress = "Scenario Address"
        };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();
        return customer;
    }

    public static async Task<Technician> AddTechnicianAsync(
        ApplicationDbContext context,
        bool isAvailable = true)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var user = CreateUser($"technician-{suffix}", $"technician-{suffix}@test.invalid", "Scenario Technician", Roles.Technician);
        var technician = new Technician
        {
            User = user,
            FirstName = "Scenario",
            LastName = "Technician",
            Specialty = "Testing",
            IsAvailable = isAvailable
        };
        context.Technicians.Add(technician);
        await context.SaveChangesAsync();
        return technician;
    }

    public static async Task<ServiceRequest> AddRequestAsync(
        ApplicationDbContext context,
        int customerId,
        DateTime scheduledDate,
        int? technicianId = null,
        string status = "Pending",
        bool isArchived = false)
    {
        var request = new ServiceRequest
        {
            CustomerID = customerId,
            TechID = technicianId,
            IssueDescription = "Deterministic integration-test request",
            ScheduledDate = scheduledDate,
            Status = status,
            CompletedDate = status == "Completed" ? scheduledDate.AddHours(1) : null,
            ServiceCategory = "Other",
            IsArchived = isArchived
        };
        context.ServiceRequests.Add(request);
        await context.SaveChangesAsync();
        return request;
    }

    public static async Task<(InventoryItem Item, JobInventoryUsage Usage)> AddInventoryAllocationAsync(
        ApplicationDbContext context,
        int requestId,
        int stockQuantity,
        int allocatedQuantity)
    {
        var item = new InventoryItem
        {
            ItemName = $"Test item {Guid.NewGuid():N}",
            SKU = $"TEST-{Guid.NewGuid():N}"[..30],
            Category = "Test",
            StockQuantity = stockQuantity,
            UnitCost = 10,
            UnitPrice = 15,
            ReorderLevel = 1
        };
        var usage = new JobInventoryUsage
        {
            RequestID = requestId,
            InventoryItem = item,
            Quantity = allocatedQuantity,
            UnitPrice = item.UnitPrice,
            IsDeducted = false
        };
        context.JobInventoryUsages.Add(usage);
        await context.SaveChangesAsync();
        return (item, usage);
    }

    private static IdentityRole CreateRole(string id, string name) => new()
    {
        Id = id,
        Name = name,
        NormalizedName = name.ToUpperInvariant(),
        ConcurrencyStamp = $"stamp-{id}"
    };

    private static ApplicationUser CreateUser(string id, string email, string fullName, string role)
    {
        var user = new ApplicationUser
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            FullName = fullName,
            Role = role,
            SecurityStamp = $"security-{id}",
            ConcurrencyStamp = $"concurrency-{id}"
        };
        user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, TestPassword);
        return user;
    }
}
