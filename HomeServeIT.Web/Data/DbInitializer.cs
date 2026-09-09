using HomeServeIT.Web.Constants;
using HomeServeIT.Web.Models;
using HomeServeIT.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace HomeServeIT.Web.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, IHostEnvironment environment)
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        if (services.GetRequiredService<IConfiguration>().GetValue<bool>("TestData:UseCurrentModel"))
        {
            RequireDisposableDatabase(context, environment);
            await context.Database.EnsureCreatedAsync();
        }
        else await context.Database.MigrateAsync();
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { Roles.Administrator, Roles.Customer, Roles.Technician })
        {
            if (!await roles.RoleExistsAsync(role))
            {
                var result = await roles.CreateAsync(new IdentityRole(role));
                if (!result.Succeeded) throw new InvalidOperationException("Could not create application roles.");
            }
        }
    }

    // Explicit maintenance command only. Normal startup never edits demo jobs or invents stock history.
    public static async Task ResetTestDataAsync(IServiceProvider services, IHostEnvironment environment, bool deleteOnly = false)
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        RequireDisposableDatabase(context, environment);
        var password = services.GetRequiredService<IConfiguration>()["TestData:Password"];
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Set TestData__Password before resetting test data.");
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var profiles = services.GetRequiredService<AccountProfileService>();
        // Validate before deleting the explicitly selected disposable database.
        foreach (var validator in users.PasswordValidators)
            if (!(await validator.ValidateAsync(users, new ApplicationUser(), password)).Succeeded)
                throw new InvalidOperationException("The test password does not meet the Identity password policy.");
        await context.Database.EnsureDeletedAsync();
        if (deleteOnly) return;
        await InitializeAsync(services, environment);
        foreach (var (name, role) in new[] { ("admin", Roles.Administrator), ("customer", Roles.Customer), ("other", Roles.Customer), ("technician", Roles.Technician) })
        {
            var email = name + "@test.invalid";
            var user = new ApplicationUser { Id = "test-" + name, FullName = "Test " + name,
                Email = email, UserName = email, EmailConfirmed = true, PhoneNumber = "+639000000001",
                StreetAddress = "Test Street", BarangayCity = "Test City" };
            var result = await profiles.CreateAsync(user, role, password);
            if (!result.Succeeded) throw new InvalidOperationException("Could not create test account: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        }
        var customer = await context.Customers.SingleAsync(c => c.UserID == "test-customer");
        var technician = await context.Technicians.SingleAsync(t => t.UserID == "test-technician");
        var request = new ServiceRequest { CustomerID = customer.CustomerID, TechID = technician.TechID,
            ScheduledDate = new DateTime(2030, 1, 15, 9, 0, 0), Status = "Diagnosing",
            IssueDescription = "Audit quotes: O'Brien \"double\" \\ path\nUnicode café 日本語 <img src=x onerror=alert(1)>",
            ServiceCategory = "Other" };
        context.ServiceRequests.Add(request);
        await context.SaveChangesAsync();
        context.Invoices.Add(new Invoice { RequestID = request.RequestID, IsQuotation = true,
            QuotationStatus = "PendingAdmin", PaymentStatus = "Unpaid", TotalAmount = 100,
            BreakdownDetails = request.IssueDescription, DateIssued = new DateTime(2030, 1, 1) });
        await context.SaveChangesAsync();
    }

    private static void RequireDisposableDatabase(ApplicationDbContext context, IHostEnvironment environment)
    {
        var connection = new MySqlConnectionStringBuilder(context.Database.GetConnectionString()!);
        if (!environment.IsDevelopment()
            || connection.Server is not ("localhost" or "127.0.0.1" or "::1")
            || !System.Text.RegularExpressions.Regex.IsMatch(connection.Database, @"^homeserve_test_[a-zA-Z0-9_]+$"))
            throw new InvalidOperationException("Test setup requires Development and a local database named homeserve_test_<name>.");
    }
}
