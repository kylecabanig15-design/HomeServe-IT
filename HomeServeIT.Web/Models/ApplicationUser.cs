using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace HomeServeIT.Web.Models;

public class ApplicationUser : IdentityUser
{
    [Required]
    [MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? StreetAddress { get; set; }

    [MaxLength(100)]
    public string? BarangayCity { get; set; }

    [MaxLength(100)]
    public string? Landmark { get; set; }

    [MaxLength(20)]
    public string? ContactMethod { get; set; }

    public bool PrefApptReminders { get; set; } = true;
    public bool PrefQuotations { get; set; } = true;
    public bool PrefInvoices { get; set; } = true;

    // Fields added to match PDF Data Dictionary strictly
    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
    
    public DateTime? LastLogin { get; set; }
    
    public bool IsArchived { get; set; } = false;
    
    [MaxLength(20)]
    public string Role { get; set; } = "Customer"; // Admin, Technician, Customer
}
