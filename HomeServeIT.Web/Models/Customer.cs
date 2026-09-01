using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HomeServeIT.Web.Models;

public class Customer
{
    [Key]
    public int CustomerID { get; set; }

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
    [MaxLength(50)]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string HomeAddress { get; set; } = string.Empty;
}
