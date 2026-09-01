using HomeServeIT.Web.Constants;
using HomeServeIT.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace HomeServeIT.Web.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider serviceProvider, IHostEnvironment env)
    {
        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var context = serviceProvider.GetRequiredService<ApplicationDbContext>();

        await context.Database.MigrateAsync();

        string[] roleNames = { Roles.Administrator, Roles.Technician, Roles.Customer };

        foreach (var roleName in roleNames)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new IdentityRole(roleName));
            }
        }

        // Role seeding must run in every environment; user/inventory seeding only in Development.
        if (!env.IsDevelopment())
            return;

        var adminUser = await CreateUserAsync(userManager, "admin@homeserveit.local", "Admin@123", Roles.Administrator, "Admin User");
        var techUser = await CreateUserAsync(userManager, "marco@homeserveit.local", "Tech@123", Roles.Technician, "Marco Reyes");
        var customerUser = await CreateUserAsync(userManager, "kyle@homeserveit.local", "Cust@123", Roles.Customer, "Kyle Cabanig");

        var uninitializedUsers = await context.Users.Where(u => u.DateCreated.Year <= 2000).ToListAsync();
        foreach (var u in uninitializedUsers)
        {
            u.DateCreated = DateTime.UtcNow;
        }
        if (uninitializedUsers.Any())
        {
            await context.SaveChangesAsync();
        }

        if (techUser != null && !context.Technicians.Any())
        {
            context.Technicians.Add(new Technician
            {
                UserID = techUser.Id,
                FirstName = "Marco",
                LastName = "Reyes",
                Specialty = "Hardware & Networking",
                IsAvailable = true
            });
            await context.SaveChangesAsync();
        }

        if (customerUser != null && !context.Customers.Any())
        {
            context.Customers.Add(new Customer
            {
                UserID = customerUser.Id,
                FirstName = "Kyle",
                LastName = "Cabanig",
                PhoneNumber = "09171234567",
                HomeAddress = "Matina, Davao City"
            });
            await context.SaveChangesAsync();
        }

        if (!context.InventoryItems.Any())
        {
            context.InventoryItems.AddRange(
                new InventoryItem { ItemName = "CAT6 Ethernet Cable (Box)", StockQuantity = 5, UnitPrice = 2500.00m, ReorderLevel = 2 },
                new InventoryItem { ItemName = "RJ45 Connectors (Pack)", StockQuantity = 20, UnitPrice = 350.00m, ReorderLevel = 5 },
                new InventoryItem { ItemName = "500GB SSD NVMe", StockQuantity = 12, UnitPrice = 1800.00m, ReorderLevel = 5 },
                new InventoryItem { ItemName = "Thermal Paste", StockQuantity = 3, UnitPrice = 450.00m, ReorderLevel = 5 }
            );
            await context.SaveChangesAsync();
        }

        var jeffreyJobs = await context.ServiceRequests
            .Include(r => r.Customer)
                .ThenInclude(c => c.User)
            .Where(r => (r.Customer.User.FullName.Contains("Jeffrey") || r.Customer.User.FullName.Contains("Caman")) && r.Status != "Completed")
            .ToListAsync();

        foreach (var j in jeffreyJobs)
        {
            j.Status = "In Progress";
        }
        if (jeffreyJobs.Any())
        {
            await context.SaveChangesAsync();
        }

        if (!context.StockMovements.Any())
        {
            var allItems = await context.InventoryItems.ToListAsync();
            var tech = await context.Technicians.Include(t => t.User).FirstOrDefaultAsync();
            var techName = tech != null ? $"{tech.FirstName} {tech.LastName} (Technician)" : "Marco Reyes (Technician)";

            var existingUsages = await context.JobInventoryUsages
                .Include(u => u.InventoryItem)
                .Include(u => u.ServiceRequest)
                    .ThenInclude(r => r.Customer)
                .Include(u => u.ServiceRequest)
                    .ThenInclude(r => r.Technician)
                .ToListAsync();

            if (existingUsages.Any())
            {
                foreach (var u in existingUsages)
                {
                    var req = u.ServiceRequest;
                    var tName = req?.Technician != null ? $"{req.Technician.FirstName} {req.Technician.LastName} (Technician)" : techName;
                    var cName = req?.Customer != null ? $"{req.Customer.FirstName} {req.Customer.LastName}" : "Customer";
                    var dest = req != null ? $"Job #JOB-{req.RequestID:D4} · {cName}{(string.IsNullOrWhiteSpace(req.Customer?.HomeAddress) ? "" : $" ({req.Customer.HomeAddress})")}" : $"Job #{u.RequestID}";

                    context.StockMovements.Add(new StockMovement
                    {
                        ItemID = u.ItemID,
                        RequestID = u.RequestID,
                        MovementType = "Job Usage",
                        Quantity = -u.Quantity,
                        UnitCost = u.InventoryItem.UnitCost,
                        UnitPrice = u.UnitPrice,
                        Timestamp = DateTime.UtcNow.AddHours(-4),
                        PerformedBy = tName,
                        DestinationOrSource = dest,
                        Notes = req != null ? $"Used for {req.ServiceCategory}: {req.IssueDescription}" : "Dispatched for service job."
                    });
                }
            }

            foreach (var item in allItems)
            {
                context.StockMovements.Add(new StockMovement
                {
                    ItemID = item.ItemID,
                    MovementType = "Initial Stock",
                    Quantity = item.StockQuantity + 2,
                    UnitCost = item.UnitCost,
                    UnitPrice = item.UnitPrice,
                    Timestamp = DateTime.UtcNow.AddDays(-7),
                    PerformedBy = "Admin User (Admin)",
                    DestinationOrSource = "Central Warehouse Inbound Shelf",
                    Notes = $"Initial inventory registration for SKU {item.SKU}."
                });

                context.StockMovements.Add(new StockMovement
                {
                    ItemID = item.ItemID,
                    MovementType = "Restock",
                    Quantity = 5,
                    UnitCost = item.UnitCost,
                    UnitPrice = item.UnitPrice,
                    Timestamp = DateTime.UtcNow.AddDays(-2),
                    PerformedBy = "Admin User (Admin)",
                    DestinationOrSource = "Supplier Inbound Restock → Main Shelf",
                    Notes = "Routine vendor replenishment order received."
                });
            }

            await context.SaveChangesAsync();
        }
    }

    private static async Task<ApplicationUser?> CreateUserAsync(UserManager<ApplicationUser> userManager, string email, string password, string role, string fullName)
    {
        var existingUser = await userManager.FindByEmailAsync(email);
        if (existingUser != null)
            return existingUser;

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = fullName,
            EmailConfirmed = true,
            Role = role,
            DateCreated = DateTime.UtcNow
        };
        
        var result = await userManager.CreateAsync(user, password);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(user, role);
            return user;
        }
        
        return null;
    }
}
