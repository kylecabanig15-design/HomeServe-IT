using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

using HomeServeIT.Web.Models;

namespace HomeServeIT.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Customer> Customers { get; set; }
    public DbSet<Technician> Technicians { get; set; }
    public DbSet<ServiceRequest> ServiceRequests { get; set; }
    public DbSet<InventoryItem> InventoryItems { get; set; }
    public DbSet<Invoice> Invoices { get; set; }
    public DbSet<ServiceMessage> ServiceMessages { get; set; }
    public DbSet<JobDeliverable> JobDeliverables { get; set; }
    public DbSet<Device> Devices { get; set; }
    public DbSet<JobInventoryUsage> JobInventoryUsages { get; set; }
    public DbSet<StockMovement> StockMovements { get; set; }
    public DbSet<SupportTicket> SupportTickets { get; set; }
    public DbSet<UserNotification> UserNotifications { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        
        // Ensure cascading deletes don't cause cycles in SQL Server/MySQL depending on config
        builder.Entity<ServiceRequest>()
            .HasOne(s => s.Customer)
            .WithMany()
            .HasForeignKey(s => s.CustomerID)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ServiceRequest>()
            .HasOne(s => s.Technician)
            .WithMany()
            .HasForeignKey(s => s.TechID)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<JobInventoryUsage>()
            .HasOne(u => u.InventoryItem)
            .WithMany()
            .HasForeignKey(u => u.ItemID)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<JobInventoryUsage>()
            .HasIndex(u => new { u.RequestID, u.ItemID })
            .IsUnique();

        builder.Entity<StockMovement>()
            .HasOne(m => m.InventoryItem)
            .WithMany()
            .HasForeignKey(m => m.ItemID)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StockMovement>()
            .HasOne(m => m.ServiceRequest)
            .WithMany()
            .HasForeignKey(m => m.RequestID)
            .OnDelete(DeleteBehavior.SetNull);

        // Cancelled services must never contribute invoices or quotations to any UI,
        // notification feed, dashboard total, or report.
        builder.Entity<Invoice>()
            .HasQueryFilter(invoice => invoice.ServiceRequest.Status != "Cancelled");

        builder.Entity<UserNotification>()
            .HasOne(n => n.RecipientUser)
            .WithMany()
            .HasForeignKey(n => n.RecipientUserID)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<UserNotification>()
            .HasIndex(n => new { n.RecipientUserID, n.SourceKey })
            .IsUnique();
    }
}
