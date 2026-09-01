using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HomeServeIT.Web.Models;

public class Device
{
    [Key]
    public int DeviceID { get; set; }

    [Required]
    public int CustomerID { get; set; }

    [ForeignKey("CustomerID")]
    public Customer Customer { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? SerialNumber { get; set; }

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
}
