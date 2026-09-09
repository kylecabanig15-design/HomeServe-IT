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
    public DbSet<ApplicationSetting> ApplicationSettings { get; set; }
    public DbSet<ApplicationSettingAudit> ApplicationSettingAudits { get; set; }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateCompletionDates();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateCompletionDates();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    private void ValidateCompletionDates()
    {
        foreach (var entry in ChangeTracker.Entries<ServiceRequest>())
        {
            if (entry.State != EntityState.Added && !(entry.State == EntityState.Modified
                && (entry.Property(r => r.CompletedDate).IsModified || entry.Property(r => r.ScheduledDate).IsModified
                    || entry.Property(r => r.Status).IsModified))) continue;
            if (entry.Entity.Status == "Completed" && (entry.Entity.CompletedDate == null
                || entry.Entity.CompletedDate > DateTime.UtcNow))
                throw new InvalidOperationException("Completion requires a recorded date that is not in the future.");
        }
    }

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

        // Keep the single-column index because MySQL requires an index whose
        // leading columns support the technician foreign key.
        builder.Entity<ServiceRequest>()
            .HasIndex(s => s.TechID);

        builder.Entity<ServiceRequest>()
            .HasIndex(s => new { s.TechID, s.ScheduledDate });

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

        builder.Entity<ApplicationSetting>()
            .Property(s => s.ConcurrencyStamp)
            .IsConcurrencyToken();

        builder.Entity<ApplicationSetting>()
            .HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(s => s.UpdatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<ApplicationSettingAudit>()
            .HasKey(a => a.AuditId);

        builder.Entity<ApplicationSettingAudit>()
            .HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(a => a.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
