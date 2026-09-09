using System.ComponentModel.DataAnnotations;

namespace HomeServeIT.Web.Models;

public sealed class ProfileViewModel : IValidatableObject
{
    [Required, StringLength(100)]
    public string FullName { get; set; } = "";
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = "";
    [StringLength(50), Phone]
    public string? Mobile { get; set; }
    [StringLength(200)]
    public string? Address { get; set; }
    [StringLength(100)]
    public string? City { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var names = (FullName ?? "").Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (names.Any(name => name.Length > 50))
            yield return new ValidationResult("First and last names must each fit within 50 characters.", [nameof(FullName)]);
        if (string.Join(", ", new[] { Address, City }.Where(s => !string.IsNullOrWhiteSpace(s))).Length > 255)
            yield return new ValidationResult("The combined address and city must fit within 255 characters.", [nameof(Address)]);
    }

    public static ProfileViewModel FromUser(ApplicationUser user) => new()
    {
        FullName = user.FullName, Email = user.Email ?? "", Mobile = user.PhoneNumber,
        Address = user.StreetAddress, City = user.BarangayCity
    };
}
