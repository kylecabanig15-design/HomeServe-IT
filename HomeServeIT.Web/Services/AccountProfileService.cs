using System.ComponentModel.DataAnnotations;
using HomeServeIT.Web.Constants;
using HomeServeIT.Web.Data;
using HomeServeIT.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HomeServeIT.Web.Services;

public sealed class AccountProfileService(ApplicationDbContext context, UserManager<ApplicationUser> users)
{
    public async Task<IdentityResult> CreateAsync(ApplicationUser user, string role, string? password = null,
        string specialty = "General service")
    {
        if (role is not (Roles.Customer or Roles.Technician or Roles.Administrator))
            return Error("Invalid role.");
        if (string.IsNullOrWhiteSpace(specialty) || specialty.Length > 255)
            return Error("Specialty must contain between 1 and 255 characters.");
        var validation = Validate(ProfileViewModel.FromUser(user));
        if (!validation.Succeeded) return validation;
        await using var transaction = await context.Database.BeginTransactionAsync();
        var result = password == null ? await users.CreateAsync(user) : await users.CreateAsync(user, password);
        if (result.Succeeded) result = await users.AddToRoleAsync(user, role);
        if (!result.Succeeded)
        {
            await transaction.RollbackAsync();
            context.ChangeTracker.Clear();
            return result;
        }
        user.Role = role;
        await EnsureDomainProfileAsync(user, role, specialty);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> UpdateAsync(ApplicationUser user, ProfileViewModel model, string? newRole = null, string? password = null,
        string? specialty = null, bool? isAvailable = null, bool? isSuspended = null)
    {
        if (specialty != null && (string.IsNullOrWhiteSpace(specialty) || specialty.Length > 255))
            return Error("Specialty must contain between 1 and 255 characters.");
        if (newRole != null && newRole is not (Roles.Customer or Roles.Technician or Roles.Administrator))
            return Error("Invalid role.");
        var validation = Validate(model);
        if (!validation.Succeeded) return validation;
        await using var transaction = await context.Database.BeginTransactionAsync();
        var email = model.Email.Trim();
        var collision = await users.FindByEmailAsync(email);
        if (collision != null && collision.Id != user.Id) return Error("That email address is already in use.");
        var emailChanged = !string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase);
        var currentRoles = await users.GetRolesAsync(user);
        if (newRole != null && currentRoles.Contains(Roles.Technician) && newRole != Roles.Technician
            && await context.ServiceRequests.AnyAsync(r => r.Technician != null && r.Technician.UserID == user.Id
                && !r.IsArchived && r.Status != "Cancelled" && r.Status != "Completed"))
            return Error("Reassign the technician's active jobs before changing their role.");
        user.FullName = model.FullName.Trim();
        user.PhoneNumber = model.Mobile?.Trim();
        user.StreetAddress = model.Address?.Trim();
        user.BarangayCity = model.City?.Trim();
        user.Email = email;
        user.UserName = email;
        if (emailChanged) user.EmailConfirmed = false;
        var result = await users.UpdateAsync(user);
        if (result.Succeeded && newRole != null && !currentRoles.Contains(newRole))
        {
            result = await users.RemoveFromRolesAsync(user, currentRoles);
            if (result.Succeeded) result = await users.AddToRoleAsync(user, newRole);
            if (result.Succeeded) user.Role = newRole;
        }
        if (result.Succeeded && !string.IsNullOrWhiteSpace(password))
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            result = await users.ResetPasswordAsync(user, token, password);
        }
        if (result.Succeeded && isSuspended.HasValue)
            result = await users.SetLockoutEndDateAsync(user, isSuspended.Value ? DateTimeOffset.MaxValue : null);
        if (!result.Succeeded)
        {
            await transaction.RollbackAsync();
            context.ChangeTracker.Clear();
            return result;
        }
        foreach (var role in await users.GetRolesAsync(user))
            await EnsureDomainProfileAsync(user, role);
        if (specialty != null || isAvailable.HasValue || isSuspended.HasValue)
        {
            var technician = await context.Technicians.SingleOrDefaultAsync(t => t.UserID == user.Id);
            if (technician != null)
            {
                if (specialty != null) technician.Specialty = specialty.Trim();
                if (isAvailable.HasValue) technician.IsAvailable = isAvailable.Value;
                if (isSuspended == true) technician.IsAvailable = false;
            }
        }
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return IdentityResult.Success;
    }

    // Caller owns the transaction so Identity, role and domain writes commit together.
    public async Task EnsureDomainProfileAsync(ApplicationUser user, string role, string specialty = "General service")
    {
        if (context.Database.CurrentTransaction == null)
            throw new InvalidOperationException("A profile write requires an owning transaction.");
        var names = user.FullName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var first = names.ElementAtOrDefault(0) ?? "User";
        var last = names.ElementAtOrDefault(1) ?? "";
        if (role == Roles.Customer)
        {
            var customer = await context.Customers.SingleOrDefaultAsync(c => c.UserID == user.Id);
            if (customer == null) { customer = new Customer { UserID = user.Id }; context.Customers.Add(customer); }
            customer.FirstName = first;
            customer.LastName = last;
            customer.PhoneNumber = user.PhoneNumber ?? "";
            customer.HomeAddress = string.Join(", ", new[] { user.StreetAddress, user.BarangayCity }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }
        if (role == Roles.Technician)
        {
            var technician = await context.Technicians.SingleOrDefaultAsync(t => t.UserID == user.Id);
            if (technician == null)
            {
                technician = new Technician { UserID = user.Id, Specialty = specialty };
                context.Technicians.Add(technician);
            }
            technician.FirstName = first;
            technician.LastName = last;
        }
    }

    private static IdentityResult Validate(ProfileViewModel model)
    {
        var errors = new List<ValidationResult>();
        return Validator.TryValidateObject(model, new ValidationContext(model), errors, true)
            ? IdentityResult.Success : Error(string.Join(" ", errors.Select(e => e.ErrorMessage)));
    }
    private static IdentityResult Error(string message) => IdentityResult.Failed(new IdentityError { Description = message });
}
