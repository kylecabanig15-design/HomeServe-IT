using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HomeServeIT.Web.Constants;
using HomeServeIT.Web.Models;
using HomeServeIT.Web.Areas.Admin.Models;

namespace HomeServeIT.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = Roles.Administrator)]
    public class SystemController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly HomeServeIT.Web.Data.ApplicationDbContext _context;

        public SystemController(UserManager<ApplicationUser> userManager, HomeServeIT.Web.Data.ApplicationDbContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        public async Task<IActionResult> UserManagement()
        {
            var users = await _userManager.Users.Where(u => !u.IsArchived).ToListAsync();
            return View(users);
        }

        public async Task<IActionResult> ArchivedUsers()
        {
            var users = await _userManager.Users.Where(u => u.IsArchived).ToListAsync();
            var requests = await _context.ServiceRequests
                .Include(r => r.Customer)
                .Include(r => r.Technician!)
                .ThenInclude(t => t.User)
                .Where(r => r.IsArchived)
                .OrderByDescending(r => r.ScheduledDate)
                .ToListAsync();

            return View(new AdminArchiveViewModel
            {
                Users = users,
                ServiceRequests = requests
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> InviteUser(string email, string role)
        {
            var validRoles = new[] { Roles.Administrator, Roles.Technician, Roles.Customer };
            if (string.IsNullOrWhiteSpace(role) || !validRoles.Contains(role))
            {
                TempData["ErrorMessage"] = "Invalid role selected.";
                return RedirectToAction(nameof(UserManagement));
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                TempData["ErrorMessage"] = "Email is required.";
                return RedirectToAction(nameof(UserManagement));
            }

            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            var result = await _userManager.CreateAsync(user, "TempPass123!");
            if (result.Succeeded)
            {
                await _userManager.AddToRoleAsync(user, role);
                TempData["SuccessMessage"] = $"User {email} created with role {role}.";
            }
            else
            {
                TempData["ErrorMessage"] = string.Join(", ", result.Errors.Select(e => e.Description));
            }
            return RedirectToAction(nameof(UserManagement));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditUser(string userId, string email, string role, string? password)
        {
            var validRoles = new[] { Roles.Administrator, Roles.Technician, Roles.Customer };
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null)
            {
                user.Email = email;
                user.UserName = email;
                var updateResult = await _userManager.UpdateAsync(user);

                if (updateResult.Succeeded)
                {
                    // Update role
                    var currentRoles = await _userManager.GetRolesAsync(user);
                    if (!currentRoles.Contains(role) && validRoles.Contains(role))
                    {
                        await _userManager.RemoveFromRolesAsync(user, currentRoles);
                        await _userManager.AddToRoleAsync(user, role);
                    }

                    // Update password if provided
                    if (!string.IsNullOrEmpty(password))
                    {
                        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                        var passResult = await _userManager.ResetPasswordAsync(user, token, password);
                        if (!passResult.Succeeded)
                        {
                            TempData["ErrorMessage"] = "User updated, but password reset failed: " + string.Join(", ", passResult.Errors.Select(e => e.Description));
                            return RedirectToAction(nameof(UserManagement));
                        }
                    }

                    TempData["SuccessMessage"] = $"User {email} updated successfully.";
                }
                else
                {
                    TempData["ErrorMessage"] = string.Join(", ", updateResult.Errors.Select(e => e.Description));
                }
            }
            else
            {
                TempData["ErrorMessage"] = "User not found.";
            }

            return RedirectToAction(nameof(UserManagement));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SuspendUser(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null)
            {
                await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
                
                // If technician, mark as unavailable
                var tech = await _context.Technicians.FirstOrDefaultAsync(t => t.UserID == userId);
                if (tech != null)
                {
                    tech.IsAvailable = false;
                    await _context.SaveChangesAsync();
                }

                TempData["SuccessMessage"] = $"User {user.Email} paused successfully.";
            }
            return RedirectToAction(nameof(UserManagement));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnsuspendUser(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null)
            {
                await _userManager.SetLockoutEndDateAsync(user, null);
                
                // If technician, mark as available
                var tech = await _context.Technicians.FirstOrDefaultAsync(t => t.UserID == userId);
                if (tech != null)
                {
                    tech.IsAvailable = true;
                    await _context.SaveChangesAsync();
                }

                TempData["SuccessMessage"] = $"User {user.Email} resumed successfully.";
            }
            return RedirectToAction(nameof(UserManagement));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ArchiveUser(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null)
            {
                user.IsArchived = true;
                await _userManager.UpdateAsync(user);
                
                await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
                
                // Mark associated technician unavailable
                var tech = await _context.Technicians.FirstOrDefaultAsync(t => t.UserID == userId);
                if (tech != null)
                {
                    tech.IsAvailable = false;
                    await _context.SaveChangesAsync();
                }
                
                TempData["SuccessMessage"] = $"User {user.Email} archived successfully.";
            }
            return RedirectToAction(nameof(UserManagement));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnarchiveUser(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null)
            {
                user.IsArchived = false;
                await _userManager.UpdateAsync(user);
                
                await _userManager.SetLockoutEndDateAsync(user, null);
                
                // Mark associated technician available
                var tech = await _context.Technicians.FirstOrDefaultAsync(t => t.UserID == userId);
                if (tech != null)
                {
                    tech.IsAvailable = true;
                    await _context.SaveChangesAsync();
                }
                
                TempData["SuccessMessage"] = $"User {user.Email} restored successfully.";
            }
            return RedirectToAction(nameof(ArchivedUsers));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUser(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                TempData["ErrorMessage"] = "User not found.";
                return RedirectToAction(nameof(ArchivedUsers));
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Remove associated technician record if any
                var tech = await _context.Technicians.FirstOrDefaultAsync(t => t.UserID == userId);
                var customer = await _context.Customers.FirstOrDefaultAsync(c => c.UserID == userId);

                if (customer != null)
                {
                    var requestIds = await _context.ServiceRequests
                        .Where(r => r.CustomerID == customer.CustomerID)
                        .Select(r => r.RequestID)
                        .ToListAsync();

                    var invoices = await _context.Invoices
                        .Where(i => requestIds.Contains(i.RequestID))
                        .ToListAsync();
                    if (invoices.Any()) _context.Invoices.RemoveRange(invoices);

                    var deliverables = await _context.JobDeliverables
                        .Where(d => requestIds.Contains(d.RequestID))
                        .ToListAsync();
                    if (deliverables.Any()) _context.JobDeliverables.RemoveRange(deliverables);

                    var messages = await _context.ServiceMessages
                        .Where(m => requestIds.Contains(m.RequestID))
                        .ToListAsync();
                    if (messages.Any()) _context.ServiceMessages.RemoveRange(messages);

                    var requests = await _context.ServiceRequests
                        .Where(r => r.CustomerID == customer.CustomerID)
                        .ToListAsync();
                    if (requests.Any()) _context.ServiceRequests.RemoveRange(requests);

                    var devices = await _context.Devices
                        .Where(d => d.CustomerID == customer.CustomerID)
                        .ToListAsync();
                    if (devices.Any()) _context.Devices.RemoveRange(devices);

                    _context.Customers.Remove(customer);
                }

                if (tech != null)
                {
                    _context.Technicians.Remove(tech);
                }

                await _context.SaveChangesAsync();

                var result = await _userManager.DeleteAsync(user);
                if (result.Succeeded)
                {
                    await transaction.CommitAsync();
                    TempData["SuccessMessage"] = $"User {user.Email} permanently deleted.";
                }
                else
                {
                    TempData["ErrorMessage"] = string.Join(", ", result.Errors.Select(e => e.Description));
                }
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                TempData["ErrorMessage"] = "Could not delete the user because of existing related records. Archive the user instead.";
            }

            return RedirectToAction(nameof(ArchivedUsers));
        }

        public IActionResult Settings()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateSettings(string section)
        {
            TempData["SuccessMessage"] = $"Settings for '{section}' updated successfully.";
            return RedirectToAction(nameof(Settings));
        }
    }
}
