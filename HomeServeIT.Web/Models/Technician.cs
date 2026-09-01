using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HomeServeIT.Web.Models;

public class Technician
{
    [Key]
    public int TechID { get; set; }

    [Required]
    public string UserID { get; set; } = string.Empty;

    [ForeignKey("UserID")]
    public ApplicationUser User { get; set; } = null!;

    [Required]
    [MaxLength(50)]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string LastName { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string Specialty { get; set; } = string.Empty;

    public bool IsAvailable { get; set; } = true;
}
